using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Areas.Identity.Services;

public class ReferenceManager
{
    private readonly IDbContextFactory<CardDbContext> _dbFactory;
    private readonly ITreasuryQuery _treasuryQuery;
    private readonly ILogger<ReferenceManager> _logger;

    public ReferenceManager(
        IDbContextFactory<CardDbContext> dbFactory, 
        ITreasuryQuery treasuryQuery,
        ILogger<ReferenceManager> logger)
    {
        _dbFactory = dbFactory;
        _treasuryQuery = treasuryQuery;
        _logger = logger;
    }


    public async Task<bool> CreateReferenceAsync(CardUser user, CancellationToken cancel = default)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        var reference = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == user.Id, cancel);

        if (reference is not null)
        {
            return false;
        }

        try
        {
            dbContext.Users.Add(new UserRef(user));

            await dbContext.SaveChangesAsync(cancel);

            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }


    public async Task<bool> UpdateReferenceAsync(CardUser user, CancellationToken cancel = default)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        var reference = await dbContext.Users
            .SingleOrDefaultAsync(u => u.Id == user.Id, cancel);

        if (reference is null)
        {
            return false;
        }

        if (reference.Name == user.Name)
        {
            return true;
        }

        try
        {
            reference.Name = user.Name;

            await dbContext.SaveChangesAsync(cancel);

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
        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        var reference = await dbContext.Users
            .SingleOrDefaultAsync(u => u.Id == user.Id, cancel);

        if (reference is null)
        {
            return false;
        }

        try
        {
            await ReturnUserDecksAsync(dbContext, reference, cancel);

            await dbContext.SaveChangesAsync(cancel);

            return true;
        }
        catch (DbUpdateException e)
        {
            _logger.LogError(e.ToString());

            return false;
        }
    }


    private async Task ReturnUserDecksAsync(
        CardDbContext dbContext, UserRef reference, CancellationToken cancel)
    {
        var userDecks = await dbContext.Decks
            .Where(d => d.OwnerId == reference.Id)
            .Include(d => d.Cards)
            .AsSplitQuery()
            .ToListAsync(cancel);

        if (!userDecks.Any())
        {
            return;
        }

        var userCards = userDecks
            .SelectMany(d => d.Cards)
            .ToList();

        var returnRequests = userCards
            .GroupBy(a => a.Card,
                (card, amounts) => 
                    new CardRequest(card, amounts.Sum(a => a.NumCopies)) );

        var returns = await _treasuryQuery.FindReturnAsync(returnRequests, cancel);

        var newTransaction = new Transaction();
        var (returnTargets, dbCopies) = returns;

        var returnChanges = returnTargets
            .Select(a => new Change
            {
                Card = a.Card,
                To = a.Location,
                // no From since deck is being deleted
                Amount = a.NumCopies - dbCopies.GetValueOrDefault(a.Id),
                Transaction = newTransaction
            });

        dbContext.AttachResult(returns);

        dbContext.Transactions.Attach(newTransaction);
        dbContext.Changes.AttachRange(returnChanges);

        dbContext.Amounts.RemoveRange(userCards);
        dbContext.Decks.RemoveRange(userDecks);

        dbContext.Users.Remove(reference);
    }
}