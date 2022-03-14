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
            .Include(b => b.Holds
                .OrderBy(h => h.Copies))
                .ThenInclude(h => h.Card)
            .OrderBy(b => b.Id);
    }


    private static IQueryable<Excess> ExistingExcess(CardDbContext dbContext)
    {
        return dbContext.Excess
            .Include(e => e.Holds
                .OrderBy(h => h.Copies))
                .ThenInclude(h => h.Card)
            .OrderBy(e => e.Id);
    }


    public static Task AddCardsAsync(
        this CardDbContext dbContext, 
        Card card, 
        int numCopies,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var request = new []{ new CardRequest(card, numCopies) };

        return dbContext.AddCardsAsync(request, cancel);
    }


    public static async Task AddCardsAsync(
        this CardDbContext dbContext,
        IEnumerable<CardRequest> adding, 
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        adding = AsAddRequests(adding); 

        if (!adding.Any())
        {
            return;
        }

        AttachCardRequests(dbContext, adding);

        await OrderedBoxes(dbContext).LoadAsync(cancel);

        await ExistingExcess(dbContext).LoadAsync(cancel);

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext);

        treasuryContext.AddExact(adding);
        treasuryContext.AddApproximate(adding);
        treasuryContext.AddGuess(adding);

        RemoveEmpty(dbContext);
    }


    private static IReadOnlyList<CardRequest> AsAddRequests(IEnumerable<CardRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        return requests
            .OfType<CardRequest>()
            .GroupBy(
                cr => cr.Card.Id,
                (_, requests) => requests.First() with
                {
                    NumCopies = requests.Sum(req => req.NumCopies)
                })
            .ToList();
    }


    private static void AttachCardRequests(
        CardDbContext dbContext, 
        IEnumerable<CardRequest> requests)
    {
        var detachedCards = requests
            .Select(cr => cr.Card)
            .Where(c => dbContext.Entry(c).State is EntityState.Detached);

        // by default treats detached cards as existing
        // new vs existing cards should be handled outside of this function

        dbContext.Cards.AttachRange(detachedCards);
    }



    public static async Task ExchangeAsync(
        this CardDbContext dbContext, 
        Deck deck, 
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(deck);

        var entry = dbContext.Entry(deck);

        if (entry.State is EntityState.Detached)
        {
            dbContext.Decks.Attach(deck);
        }

        await OrderedBoxes(dbContext).LoadAsync(cancel);

        await ExistingExcess(dbContext).LoadAsync(cancel);

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext);
        var exchangeContext = new ExchangeContext(dbContext, treasuryContext);

        exchangeContext.TakeExact();
        exchangeContext.TakeApproximate();

        exchangeContext.ReturnExact();
        exchangeContext.ReturnApproximate();
        exchangeContext.ReturnGuess();

        treasuryContext.LowerExactExcess();
        treasuryContext.LowerApproximateExcess();

        RemoveEmpty(dbContext);
    }



    public static async Task UpdateBoxesAsync(
        this CardDbContext dbContext,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (AreBoxesUnchanged(dbContext) && AreHoldsUnchanged(dbContext))
        {
            return;
        }

        await OrderedBoxes(dbContext).LoadAsync(cancel);

        await ExistingExcess(dbContext).LoadAsync(cancel);

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext);

        treasuryContext.LowerExactOver();
        treasuryContext.LowerApproximateOver();

        treasuryContext.LowerExactExcess();
        treasuryContext.LowerApproximateExcess();

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


    private static bool AreHoldsUnchanged(CardDbContext dbContext)
    {
        return dbContext.ChangeTracker
            .Entries<Hold>()
            .All(e => e.State is EntityState.Detached
                || e.State is not EntityState.Added
                    && !e.Property(h => h.Copies).IsModified);
    }


    private static void AddMissingExcess(CardDbContext dbContext)
    {
        if (!dbContext.Excess.Local.Any())
        {
            var excessBox = Excess.Create();

            dbContext.Excess.Add(excessBox);
        }
    }


    private static void RemoveEmpty(CardDbContext dbContext)
    {
        var emptyHolds = dbContext.Holds.Local
            .Where(h => h.Copies == 0);

        var emptyWants = dbContext.Wants.Local
            .Where(w => w.Copies == 0);

        var emptyGiveBacks = dbContext.GiveBacks.Local
            .Where(g => g.Copies == 0);

        var emptyTransactions = dbContext.Transactions.Local
            .Where(t => !t.Changes.Any());

        dbContext.Holds.RemoveRange(emptyHolds);
        dbContext.Wants.RemoveRange(emptyWants);
        dbContext.GiveBacks.RemoveRange(emptyGiveBacks);

        dbContext.Transactions.RemoveRange(emptyTransactions);
    }
}
