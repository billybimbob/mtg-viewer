using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Areas.Identity.Services;

public class ReferenceManager
{
    private readonly CardDbContext _dbContext;
    private readonly ILogger<ReferenceManager> _logger;

    public ReferenceManager(
        CardDbContext dbContext, ILogger<ReferenceManager> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }


    public IQueryable<UserRef> References =>
        _dbContext.Users.AsNoTrackingWithIdentityResolution();


    public async Task<bool> CreateReferenceAsync(CardUser user, CancellationToken cancel = default)
    {
        var reference = await _dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == user.Id, cancel);

        if (reference is not null)
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
        var reference = await _dbContext.Users
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
        var reference = await _dbContext.Users
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
            .ToListAsync(cancel);

        if (!userDecks.Any())
        {
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
}
