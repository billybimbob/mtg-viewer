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
            _logger.LogError(e.ToString());

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
            .OrderBy(u => u.Id)
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
            _logger.LogError(e.ToString());

            return false;
        }
    }


    private async Task ReturnUserDecksAsync(UserRef reference, CancellationToken cancel)
    {
        var userDecks = await _dbContext.Decks
            .Where(d => d.OwnerId == reference.Id)
            .Include(d => d.Cards)
                .ThenInclude(a => a.Card)
            .ToListAsync(cancel);


        if (!userDecks.Any())
        {
            _dbContext.Users.Remove(reference);
            return;
        }

        var userCards = userDecks
            .SelectMany(d => d.Cards)
            .ToList();

        _dbContext.Amounts.RemoveRange(userCards);
        _dbContext.Decks.RemoveRange(userDecks);
        _dbContext.Users.Remove(reference);

        var returnRequests = userCards
            .GroupBy(a => a.Card,
                (card, amounts) => 
                    new CardRequest(card, amounts.Sum(a => a.NumCopies)) );

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

        await transaction.CommitAsync();
    }
}
