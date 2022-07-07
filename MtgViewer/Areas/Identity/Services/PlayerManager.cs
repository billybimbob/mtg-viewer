using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Services.Infrastructure;

namespace MtgViewer.Areas.Identity.Services;

public class PlayerManager
{
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private readonly ResetHandler _resetHandler;
    private readonly ILogger<PlayerManager> _logger;

    public PlayerManager(
        CardDbContext dbContext,
        UserManager<CardUser> userManager,
        ResetHandler resetHandler,
        ILogger<PlayerManager> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _resetHandler = resetHandler;
        _logger = logger;
    }

    public IQueryable<Player> Players
        => _dbContext.Players.AsNoTrackingWithIdentityResolution();

    public async Task<bool> CreateAsync(CardUser user, CancellationToken cancel = default)
    {
        bool validUser = await _userManager.Users
            .AnyAsync(u => u.Id == user.Id, cancel);

        if (!validUser)
        {
            return false;
        }

        bool isExisting = await _dbContext.Players
            .AnyAsync(p => p.Id == user.Id, cancel);

        if (isExisting)
        {
            return false;
        }

        try
        {
            _dbContext.Players.Add(new Player(user));

            await _dbContext.SaveChangesAsync(cancel);

            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    public async Task<bool> UpdateAsync(CardUser user, CancellationToken cancel = default)
    {
        bool isValidUser = await _userManager.Users
            .AnyAsync(u => u.Id == user.Id, cancel);

        if (!isValidUser)
        {
            return false;
        }

        var player = await _dbContext.Players
            .OrderBy(p => p.Id)
            .SingleOrDefaultAsync(p => p.Id == user.Id, cancel);

        if (player is null)
        {
            return false;
        }

        if (player.Name == user.DisplayName)
        {
            return true;
        }

        try
        {
            player.Name = user.DisplayName;

            await _dbContext.SaveChangesAsync(cancel);

            return true;
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("{Error}", e);

            return false;
        }
    }

    public async Task<bool> DeleteAsync(CardUser user, CancellationToken cancel = default)
    {
        bool isValidUser = await _userManager.Users
            .AnyAsync(u => u.Id == user.Id, cancel);

        if (isValidUser)
        {
            // only delete reference if the actual user is already deleted
            return false;
        }

        var player = await _dbContext.Players
            .Include(p => p.Decks)
                .ThenInclude(d => d.Holds)
                .ThenInclude(h => h.Card)

            .OrderBy(p => p.Id)
            .AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == user.Id, cancel);

        if (player is null)
        {
            return false;
        }

        try
        {
            await ReturnPlayerDecksAsync(player, cancel);

            await _dbContext.SaveChangesAsync(cancel);

            return true;
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("{Error}", e);

            return false;
        }
    }

    private async Task ReturnPlayerDecksAsync(Player player, CancellationToken cancel)
    {
        if (!player.Decks.Any())
        {
            _dbContext.Players.Remove(player);
            return;
        }

        var playerHolds = player.Decks
            .SelectMany(d => d.Holds)
            .ToList();

        _dbContext.Holds.RemoveRange(playerHolds);
        _dbContext.Decks.RemoveRange(player.Decks);
        _dbContext.Players.Remove(player);

        var returnRequests = playerHolds
            .GroupBy(h => h.Card,
                (card, holds) =>
                    new CardRequest(card, holds.Sum(h => h.Copies)));

        await _dbContext.AddCardsAsync(returnRequests, cancel);
    }

    public async Task ResetAsync(CancellationToken cancel = default)
    {
        // keep eye on, reset changes are not atomic

        await _resetHandler.ResetAsync(cancel);

        var resetPlayers = _dbContext.Players
            .Where(p => p.ResetRequested)
            .AsAsyncEnumerable()
            .WithCancellation(cancel);

        var resettingIds = new HashSet<string>();

        await foreach (var player in resetPlayers)
        {
            player.ResetRequested = false;

            resettingIds.Add(player.Id);
        }

        await _dbContext.SaveChangesAsync(cancel);

        var cardUsers = await _userManager.Users
            .Where(u => resettingIds.Contains(u.Id))
            .ToListAsync(cancel);

        foreach (var cardUser in cardUsers)
        {
            await _userManager.AddClaimAsync(
                cardUser, new Claim(CardClaims.ChangeTreasury, cardUser.Id));
        }
    }
}
