using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Areas.Identity.Services;

public class ReferenceManager
{
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private readonly BulkOperations _bulkOperations;
    private readonly ILogger<ReferenceManager> _logger;

    public ReferenceManager(
        CardDbContext dbContext,
        UserManager<CardUser> userManager,
        BulkOperations bulkOperations,
        ILogger<ReferenceManager> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _bulkOperations = bulkOperations;
        _logger = logger;
    }

    public IQueryable<UserRef> References =>
        _dbContext.Users.AsNoTrackingWithIdentityResolution();

    public async Task<bool> CreateReferenceAsync(CardUser user, CancellationToken cancel = default)
    {
        bool validUser = await _userManager.Users
            .AnyAsync(u => u.Id == user.Id, cancel);

        if (!validUser)
        {
            return false;
        }

        bool existing = await _dbContext.Users
            .AnyAsync(u => u.Id == user.Id, cancel);

        if (existing)
        {
            return false;
        }

        try
        {
            _dbContext.Users.Add(new UserRef(user));

            await _dbContext.SaveChangesAsync(cancel);

            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    public async Task<bool> UpdateReferenceAsync(CardUser user, CancellationToken cancel = default)
    {
        bool validUser = await _userManager.Users
            .AnyAsync(u => u.Id == user.Id, cancel);

        if (!validUser)
        {
            return false;
        }

        var reference = await _dbContext.Users
            .OrderBy(u => u.Id)
            .SingleOrDefaultAsync(u => u.Id == user.Id, cancel);

        if (reference is null)
        {
            return false;
        }

        if (reference.Name == user.DisplayName)
        {
            return true;
        }

        try
        {
            reference.Name = user.DisplayName;

            await _dbContext.SaveChangesAsync(cancel);

            return true;
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("{Error}", e);

            return false;
        }
    }

    public async Task<bool> DeleteReferenceAsync(CardUser user, CancellationToken cancel = default)
    {
        bool validUser = await _userManager.Users
            .AnyAsync(u => u.Id == user.Id, cancel);

        if (validUser)
        {
            return false;
        }

        var reference = await _dbContext.Users
            .Include(u => u.Decks)
                .ThenInclude(d => d.Holds)
                .ThenInclude(h => h.Card)

            .OrderBy(u => u.Id)
            .AsSplitQuery()
            .SingleOrDefaultAsync(u => u.Id == user.Id, cancel);

        if (reference is null)
        {
            return false;
        }

        try
        {
            await ReturnUserDecksAsync(reference, cancel);

            await _dbContext.SaveChangesAsync(cancel);

            return true;
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("{Error}", e);

            return false;
        }
    }

    private async Task ReturnUserDecksAsync(UserRef reference, CancellationToken cancel)
    {
        if (!reference.Decks.Any())
        {
            _dbContext.Users.Remove(reference);
            return;
        }

        var userHolds = reference.Decks
            .SelectMany(d => d.Holds)
            .ToList();

        _dbContext.Holds.RemoveRange(userHolds);
        _dbContext.Decks.RemoveRange(reference.Decks);
        _dbContext.Users.Remove(reference);

        var returnRequests = userHolds
            .GroupBy(h => h.Card,
                (card, holds) =>
                    new CardRequest(card, holds.Sum(h => h.Copies)));

        await _dbContext.AddCardsAsync(returnRequests, cancel);
    }

    public async Task ApplyResetAsync(CancellationToken cancel = default)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(cancel);

        await _bulkOperations.ResetAsync(cancel);

        var usersResetting = await _dbContext.Users
            .Where(u => u.ResetRequested)
            .ToListAsync(cancel);

        var resettingIds = usersResetting
            .Select(u => u.Id)
            .ToArray();

        var cardUsers = await _userManager.Users
            .Where(u => resettingIds.Contains(u.Id))
            .ToListAsync(cancel);

        foreach (var cardUser in cardUsers)
        {
            await _userManager.AddClaimAsync(
                cardUser, new Claim(CardClaims.ChangeTreasury, cardUser.Id));
        }

        foreach (var reference in usersResetting)
        {
            reference.ResetRequested = false;
        }

        await _dbContext.SaveChangesAsync(cancel);

        await transaction.CommitAsync(cancel);
    }
}
