using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MTGViewer.Data.Internal;

namespace MTGViewer.Data;

internal sealed record StorageSpace
{
    public int Id { get; init; }

    public string Name { get; init; } = default!;

    public int Held { get; set; }

    public int? Capacity { get; init; }

    public bool HasSpace => Held < Capacity || Capacity is null;

    public int? Remaining => Capacity - Held;
}

public static partial class TreasuryExtensions
{
    private static IQueryable<StorageSpace> StorageSpaces(CardDbContext dbContext)
    {
        return dbContext.Locations
            .Where(l => l is Box || l is Excess)
            .OrderBy(l => l.Id)

            .Select(l => new StorageSpace
            {
                Id = l.Id,
                Name = l.Name,
                Held = l.Holds.Sum(h => h.Copies),
                Capacity = l is Box ? (l as Box)!.Capacity : null
            });
    }

    private static IQueryable<Location> MatchingStorage(CardDbContext dbContext, string[] cardNames)
    {
        return dbContext.Locations
            .Where(l => l is Excess
                || l is Box && l.Holds.Any(h => cardNames.Contains(h.Card.Name)))

            .Include(l => l.Holds
                .Where(h => cardNames.Contains(h.Card.Name))
                .OrderBy(h => h.Card.Name)
                    .ThenByDescending(h => h.Copies))
                .ThenInclude(h => h.Card)

            .OrderBy(l => l.Id);
    }

    private static IQueryable<Location> ArrangingStorage(CardDbContext dbContext, int[] modified)
    {
        // modified locations won't be overridden

        return dbContext.Locations
            .Where(l => modified.Contains(l.Id)
                || l is Excess
                || l is Box && (l as Box)!.Capacity < l.Holds.Sum(h => h.Copies))

            .Include(l => l.Holds
                .OrderBy(h => h.Card.Name)
                    .ThenByDescending(h => h.Copies))
                .ThenInclude(h => h.Card)

            .OrderBy(l => l.Id);
    }

    private static IQueryable<Location> EntireStorage(CardDbContext dbContext)
    {
        // loading all shared cards, could be memory inefficient
        // TODO: find more efficient way to determining card position
        // unbounded: keep eye on

        return dbContext.Locations
            .Where(l => l is Excess || l is Box)

            .Include(l => l.Holds
                .OrderBy(h => h.Card.Name)
                    .ThenByDescending(h => h.Copies))
                .ThenInclude(h => h.Card)

            .OrderBy(l => l.Id);
    }

    public static Task AddCardsAsync(
        this CardDbContext dbContext,
        Card card,
        int copies,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var request = new[] { new CardRequest(card, copies) };

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

        var storageSpaces = await StorageSpaces(dbContext)
            .ToDictionaryAsync(
                s => (LocationIndex)s, cancel);

        var cardNames = adding
            .Select(cr => cr.Card.Name)
            .Distinct()
            .ToArray();

        await MatchingStorage(dbContext, cardNames).LoadAsync(cancel);

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext, storageSpaces);

        treasuryContext.AddExact(adding);
        treasuryContext.AddApproximate(adding);

        if (adding.Any(cr => cr.Copies > 0))
        {
            await EntireStorage(dbContext).LoadAsync(cancel);

            treasuryContext.Refresh();
            treasuryContext.AddGuess(adding);
        }

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
                    Copies = requests.Sum(req => req.Copies)
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

        var storageSpaces = await StorageSpaces(dbContext)
            .ToDictionaryAsync(
                s => (LocationIndex)s, cancel);

        var cardNames = deck.Wants
            .Select(w => w.Card.Name)
            .Union(deck.Givebacks
                .Select(g => g.Card.Name))
            .ToArray();

        await MatchingStorage(dbContext, cardNames).LoadAsync(cancel);

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext, storageSpaces);
        var exchangeContext = new ExchangeContext(dbContext, treasuryContext);

        bool isFullyLoaded = false;

        exchangeContext.TakeExact();
        exchangeContext.TakeApproximate();

        exchangeContext.ReturnExact();
        exchangeContext.ReturnApproximate();

        if (deck.Givebacks.Any(g => g.Copies > 0))
        {
            await EntireStorage(dbContext).LoadAsync(cancel);

            isFullyLoaded = true;

            treasuryContext.Refresh();

            exchangeContext.ReturnGuess();
        }

        bool hasAvialable = treasuryContext.Available.Any();

        if (hasAvialable && !isFullyLoaded)
        {
            await EntireStorage(dbContext).LoadAsync(cancel);

            treasuryContext.Refresh();
        }

        if (hasAvialable)
        {
            treasuryContext.LowerExactExcess();
            treasuryContext.LowerApproximateExcess();
        }

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

        var storageSpaces = await MergedStorageSpacesAsync(dbContext, cancel);

        var modified = dbContext.Boxes.Local
            .Where(b => dbContext.Entry(b).State is EntityState.Modified)
            .Select(b => b.Id)
            .ToArray();

        await ArrangingStorage(dbContext, modified).LoadAsync(cancel);

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext, storageSpaces);

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

    private static async Task<Dictionary<LocationIndex, StorageSpace>> MergedStorageSpacesAsync(
        CardDbContext dbContext,
        CancellationToken cancel)
    {
        var storageSpaces = await StorageSpaces(dbContext)
            .ToDictionaryAsync(s => (LocationIndex)s, cancel);

        var modifiedBoxes = dbContext.Boxes.Local
            .Where(b => dbContext.Entry(b).State is EntityState.Modified)
            .Join(storageSpaces,
                b => b.Id,
                kv => kv.Key.Id,
                (Box, kv) => (kv.Key, kv.Value, Box));

        foreach (var (index, space, box) in modifiedBoxes)
        {
            storageSpaces.Remove(index);

            storageSpaces.Add(
                (LocationIndex)box, space with { Capacity = box.Capacity });
        }

        return storageSpaces;
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

        var emptyGivebacks = dbContext.Givebacks.Local
            .Where(g => g.Copies == 0);

        var emptyTransactions = dbContext.Transactions.Local
            .Where(t => !t.Changes.Any());

        dbContext.Holds.RemoveRange(emptyHolds);
        dbContext.Wants.RemoveRange(emptyWants);
        dbContext.Givebacks.RemoveRange(emptyGivebacks);

        dbContext.Transactions.RemoveRange(emptyTransactions);
    }
}
