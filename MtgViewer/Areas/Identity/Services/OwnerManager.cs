using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Services.Infrastructure;

namespace MtgViewer.Areas.Identity.Services;

public class OwnerManager
{
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private readonly ResetHandler _resetHandler;
    private readonly ILogger<OwnerManager> _logger;

    public OwnerManager(
        CardDbContext dbContext,
        UserManager<CardUser> userManager,
        ResetHandler resetHandler,
        ILogger<OwnerManager> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _resetHandler = resetHandler;
        _logger = logger;
    }

    public IQueryable<Owner> Owners =>
        _dbContext.Owners.AsNoTrackingWithIdentityResolution();

    public async Task<bool> CreateAsync(CardUser user, CancellationToken cancel = default)
    {
        bool validUser = await _userManager.Users
            .AnyAsync(u => u.Id == user.Id, cancel);

        if (!validUser)
        {
            return false;
        }

        bool existing = await _dbContext.Owners
            .AnyAsync(o => o.Id == user.Id, cancel);

        if (existing)
        {
            return false;
        }

        try
        {
            _dbContext.Owners.Add(new Owner(user));

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
        bool validUser = await _userManager.Users
            .AnyAsync(u => u.Id == user.Id, cancel);

        if (!validUser)
        {
            return false;
        }

        var owner = await _dbContext.Owners
            .OrderBy(o => o.Id)
            .SingleOrDefaultAsync(o => o.Id == user.Id, cancel);

        if (owner is null)
        {
            return false;
        }

        if (owner.Name == user.DisplayName)
        {
            return true;
        }

        try
        {
            owner.Name = user.DisplayName;

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

        var owner = await _dbContext.Owners
            .Include(o => o.Decks)
                .ThenInclude(d => d.Holds)
                .ThenInclude(h => h.Card)

            .OrderBy(o => o.Id)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == user.Id, cancel);

        if (owner is null)
        {
            return false;
        }

        try
        {
            await ReturnOwnedDecksAsync(owner, cancel);

            await _dbContext.SaveChangesAsync(cancel);

            return true;
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("{Error}", e);

            return false;
        }
    }

    private async Task ReturnOwnedDecksAsync(Owner owner, CancellationToken cancel)
    {
        if (!owner.Decks.Any())
        {
            _dbContext.Owners.Remove(owner);
            return;
        }

        var ownedHolds = owner.Decks
            .SelectMany(d => d.Holds)
            .ToList();

        _dbContext.Holds.RemoveRange(ownedHolds);
        _dbContext.Decks.RemoveRange(owner.Decks);
        _dbContext.Owners.Remove(owner);

        var returnRequests = ownedHolds
            .GroupBy(h => h.Card,
                (card, holds) =>
                    new CardRequest(card, holds.Sum(h => h.Copies)));

        await _dbContext.AddCardsAsync(returnRequests, cancel);
    }

    public async Task ResetAsync(CancellationToken cancel = default)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(cancel);

        await _resetHandler.ResetAsync(cancel);

        var resetOwners = await _dbContext.Owners
            .Where(o => o.ResetRequested)
            .ToListAsync(cancel);

        string[] resettingIds = resetOwners
            .Select(o => o.Id)
            .ToArray();

        var cardUsers = await _userManager.Users
            .Where(u => resettingIds.Contains(u.Id))
            .ToListAsync(cancel);

        foreach (var cardUser in cardUsers)
        {
            await _userManager.AddClaimAsync(
                cardUser, new Claim(CardClaims.ChangeTreasury, cardUser.Id));
        }

        foreach (var owner in resetOwners)
        {
            owner.ResetRequested = false;
        }

        await _dbContext.SaveChangesAsync(cancel);

        await transaction.CommitAsync(cancel);
    }
}
