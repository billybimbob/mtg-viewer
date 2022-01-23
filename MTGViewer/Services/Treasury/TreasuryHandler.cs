using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;
using MTGViewer.Services.Internal;

namespace MTGViewer.Services;

public class TreasuryHandler
{
    private readonly ILogger<TreasuryHandler> _logger;

    public TreasuryHandler(ILogger<TreasuryHandler> logger)
    {
        _logger = logger;
    }


    private static IQueryable<Box> OrderedBoxes(CardDbContext dbContext)
    {
        // loading all shared cards, could be memory inefficient
        // TODO: find more efficient way to determining card position
        // unbounded: keep eye on

        return dbContext.Boxes
            .Include(b => b.Cards
                .OrderBy(a => a.NumCopies))
                .ThenInclude(a => a.Card)
            .OrderBy(b => b.Id);
    }



    #region Add

    public Task AddAsync(
        CardDbContext dbContext, 
        Card card, 
        int numCopies,
        CancellationToken cancel = default)
    {
        var request = new []{ new CardRequest(card, numCopies) };

        return AddAsync(dbContext, request, cancel);
    }


    public async Task AddAsync(
        CardDbContext dbContext,
        IEnumerable<CardRequest> adding, 
        CancellationToken cancel = default)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        adding = AsAddRequests(adding); 

        if (!adding.Any())
        {
            return;
        }

        var cards = adding.Select(cr => cr.Card);

        dbContext.Cards.AttachRange(cards);

        await OrderedBoxes(dbContext).LoadAsync(cancel); 

        if (!dbContext.Boxes.Local.Any())
        {
            throw new InvalidOperationException("There are no boxes to return to");
        }

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext);

        AddCopies(treasuryContext, adding, AddScheme.Exact);
        AddCopies(treasuryContext, adding, AddScheme.Approximate);
        AddCopies(treasuryContext, adding, AddScheme.Guess);

        RemoveEmpty(dbContext);
    }


    private static IReadOnlyList<CardRequest> AsAddRequests(IEnumerable<CardRequest?>? requests)
    {
        if (requests is null)
        {
            throw new ArgumentNullException(nameof(requests));
        }

        requests = requests.ToArray();

        if (requests.Any(req => req is null))
        {
            throw new ArgumentNullException(nameof(requests));
        }

        // descending so that the first added cards do not shift down the 
        // positioning of the sorted card amounts
        // each of the returned cards should have less effect on following returns
        // keep eye on

        return requests
            .Cast<CardRequest>()
            .GroupBy(
                req => req.Card.Id,
                (_, requests) => requests.First() with
                {
                    NumCopies = requests.Sum(req => req.NumCopies)
                })
            .OrderByDescending(req => req.Card.Name)
                .ThenByDescending(req => req.Card.SetName)
            .ToList();
    }


    private static void AddCopies(
        TreasuryContext treasuryContext, 
        IEnumerable<CardRequest> requests,
        AddScheme scheme)
    {
        var adds = treasuryContext.AddAssignment(requests, scheme);

        foreach ((CardRequest request, int numCopies, Box box) in adds)
        {
            treasuryContext.AddCopies(request.Card, numCopies, box);
            request.NumCopies -= numCopies;
        }
    }

    #endregion



    #region Exchange

    public async Task ExchangeAsync(
        CardDbContext dbContext, 
        Deck deck, 
        CancellationToken cancel = default)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        if (deck is null)
        {
            throw new ArgumentNullException(nameof(deck));
        }

        var entry = dbContext.Entry(deck);

        if (entry.State is EntityState.Detached)
        {
            dbContext.Decks.Attach(deck);
        }

        await OrderedBoxes(dbContext).LoadAsync(cancel);

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext);
        var exchangeContext = new ExchangeContext(dbContext, treasuryContext);

        TakeCopies(treasuryContext, exchangeContext, TakeScheme.Exact);
        TakeCopies(treasuryContext, exchangeContext, TakeScheme.Approximate);

        ReturnCopies(treasuryContext, exchangeContext, ReturnScheme.Exact);
        ReturnCopies(treasuryContext, exchangeContext, ReturnScheme.Approximate);
        ReturnCopies(treasuryContext, exchangeContext, ReturnScheme.Guess);

        Transfer(treasuryContext, TransferScheme.Exact);
        Transfer(treasuryContext, TransferScheme.Approximate);

        RemoveEmpty(dbContext);
    }


    private static void TakeCopies(
        TreasuryContext treasuryContext, 
        ExchangeContext exchangeContext,
        TakeScheme scheme)
    {
        var checkouts = treasuryContext.TakeAssignment(exchangeContext, scheme);

        foreach ((Card card, int numCopies, Box box) in checkouts)
        {
            exchangeContext.TakeCopies(card, numCopies, box);
        }
    }


    private static void ReturnCopies(
        TreasuryContext treasuryContext,
        ExchangeContext exchangeContext,
        ReturnScheme scheme)
    {
        var returns = treasuryContext.ReturnAssignment(exchangeContext, scheme);

        foreach ((Card card, int numCopies, Box box) in returns)
        {
            exchangeContext.ReturnCopies(card, numCopies, box);
        }
    }

    #endregion



    #region Update

    public async Task UpdateAsync(
        CardDbContext dbContext,
        Box updated, 
        CancellationToken cancel = default)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        if (updated is null)
        {
            throw new ArgumentNullException(nameof(updated));
        }

        if (await IsBoxUnchangedAsync(dbContext, updated, cancel))
        {
            return;
        }

        await OrderedBoxes(dbContext).LoadAsync(cancel);

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext);

        Transfer(treasuryContext, TransferScheme.Exact);
        Transfer(treasuryContext, TransferScheme.Approximate);
        Transfer(treasuryContext, TransferScheme.Overflow);

        RemoveEmpty(dbContext);
    }


    private static async Task<bool> IsBoxUnchangedAsync(
        CardDbContext dbContext, Box updated, CancellationToken cancel)
    {
        var entry = dbContext.Entry(updated);

        if (entry.State is EntityState.Detached)
        {
            dbContext.Boxes.Update(updated);
        }

        var capacityProperty = entry.Property(b => b.Capacity);

        if (capacityProperty.CurrentValue != capacityProperty.OriginalValue)
        {
            return false;
        }

        return await entry.GetDatabaseValuesAsync(cancel) is var dbValues
            && capacityProperty.Metadata is var capacityMeta
            && dbValues?.GetValue<int?>(capacityMeta) == updated.Capacity;
    }

    #endregion


    private static void Transfer(
        TreasuryContext treasuryContext, 
        TransferScheme scheme)
    {
        var transfers = treasuryContext.TransferAssignment(scheme);

        foreach ((Amount source, int numCopies, Box box) in transfers)
        {
            treasuryContext.TransferCopies(source.Card, numCopies, box, source.Location);
        }
    }


    private static void AddMissingExcess(CardDbContext dbContext)
    {
        if (!dbContext.Boxes.Local.Any(b => b.IsExcess))
        {
            var excessBox = new Box
            {
                Name = "Excess",
                Capacity = 0,
                Bin = new Bin
                {
                    Name = "Excess"
                }
            };
            
            dbContext.Boxes.Add(excessBox);
        }
    }


    private static void RemoveEmpty(CardDbContext dbContext)
    {
        var emptyAmounts = dbContext.Amounts.Local
            .Where(a => a.NumCopies == 0);

        var emptyWants = dbContext.Wants.Local
            .Where(w => w.NumCopies == 0);

        var emptyGiveBacks = dbContext.GiveBacks.Local
            .Where(g => g.NumCopies == 0);

        var emptyTransactions = dbContext.Transactions.Local
            .Where(t => !t.Changes.Any());

        dbContext.Amounts.RemoveRange(emptyAmounts);
        dbContext.Wants.RemoveRange(emptyWants);
        dbContext.GiveBacks.RemoveRange(emptyGiveBacks);

        dbContext.Transactions.RemoveRange(emptyTransactions);
    }
}