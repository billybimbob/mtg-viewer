using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using MTGViewer.Data.Internal;

namespace MTGViewer.Data;

public static partial class TreasuryExtensions
{
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

    public static Task AddCardsAsync(
        this CardDbContext dbContext, 
        Card card, 
        int numCopies,
        CancellationToken cancel = default)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        var request = new []{ new CardRequest(card, numCopies) };

        return dbContext.AddCardsAsync(request, cancel);
    }


    public static async Task AddCardsAsync(
        this CardDbContext dbContext,
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

        AttachCardRequests(dbContext, adding);

        await OrderedBoxes(dbContext).LoadAsync(cancel); 

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
                cr => cr.Card.Id,
                (_, requests) => requests.First() with
                {
                    NumCopies = requests.Sum(req => req.NumCopies)
                })
            .OrderByDescending(cr => cr.Card.Name)
                .ThenByDescending(cr => cr.Card.SetName)
            .ToList();
    }


    private static void AttachCardRequests(
        CardDbContext dbContext, 
        IEnumerable<CardRequest> requests)
    {
        var detachedCards = requests
            .Select(cr => cr.Card)
            .Where(c => dbContext.Entry(c).State is EntityState.Detached);

        dbContext.Cards.AttachRange(detachedCards);
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

    public static async Task ExchangeAsync(
        this CardDbContext dbContext, 
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

        TransferExcessCopies(treasuryContext, ExcessScheme.Exact);
        TransferExcessCopies(treasuryContext, ExcessScheme.Approximate);

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



    #region Update Boxes

    public static async Task UpdateBoxesAsync(
        this CardDbContext dbContext,
        CancellationToken cancel = default)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        if (AreBoxesUnchanged(dbContext) && AreAmountsUnchanged(dbContext))
        {
            return;
        }

        await OrderedBoxes(dbContext).LoadAsync(cancel);

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext);

        TransferOverflowCopies(treasuryContext, OverflowScheme.Exact);
        TransferOverflowCopies(treasuryContext, OverflowScheme.Approximate);

        TransferExcessCopies(treasuryContext, ExcessScheme.Exact);
        TransferExcessCopies(treasuryContext, ExcessScheme.Approximate);

        RemoveEmpty(dbContext);
    }


    private static bool AreBoxesUnchanged(CardDbContext dbContext)
    {
        return dbContext.ChangeTracker
            .Entries<Box>()
            .All(e => e.State is EntityState.Detached
                || e.State is not EntityState.Added
                && !e.Property(b => b.Capacity).IsModified);
    }


    private static bool AreAmountsUnchanged(CardDbContext dbContext)
    {
        return dbContext.ChangeTracker
            .Entries<Amount>()
            .All(e => e.State is EntityState.Detached
                || e.State is not EntityState.Added
                && !e.Property(a => a.NumCopies).IsModified);
    }


    private static void TransferOverflowCopies(TreasuryContext treasuryContext, OverflowScheme scheme)
    {
        var transfers = treasuryContext.OverflowAssignment(scheme);

        foreach ((Amount source, int numCopies, Box box) in transfers)
        {
            treasuryContext.TransferCopies(source.Card, numCopies, box, source.Location);
        }
    }

    #endregion


    private static void TransferExcessCopies(
        TreasuryContext treasuryContext, 
        ExcessScheme scheme)
    {
        var transfers = treasuryContext.ExcessAssignment(scheme);

        foreach ((Amount source, int numCopies, Box box) in transfers)
        {
            treasuryContext.TransferCopies(source.Card, numCopies, box, source.Location);
        }
    }


    private static void AddMissingExcess(CardDbContext dbContext)
    {
        if (!dbContext.Boxes.Local.Any(b => b.IsExcess))
        {
            var excessBox = Box.CreateExcess();

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
