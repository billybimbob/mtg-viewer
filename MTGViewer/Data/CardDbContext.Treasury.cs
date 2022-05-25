using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using MTGViewer.Data.Treasury;

namespace MTGViewer.Data;

public partial class CardDbContext
{
    public Task AddCardsAsync(Card card, int copies, CancellationToken cancel = default)
    {
        var request = new[] { new CardRequest(card, copies) };

        return AddCardsAsync(request, cancel);
    }

    public async Task AddCardsAsync(IEnumerable<CardRequest> adding, CancellationToken cancel = default)
    {
        adding = AsAddRequests(adding);

        if (!adding.Any())
        {
            return;
        }

        AttachCardRequests(adding);

        var storageSpaces = await StorageSpaces()
            .ToDictionaryAsync(s => (LocationIndex)s, cancel);

        string[] cardNames = adding
            .Select(cr => cr.Card.Name)
            .Distinct()
            .ToArray();

        await MatchingStorage(cardNames).LoadAsync(cancel);

        var treasuryContext = new TreasuryContext(this, storageSpaces);

        treasuryContext.AddExact(adding);
        treasuryContext.AddApproximate(adding);

        if (adding.Any(cr => cr.Copies > 0))
        {
            await treasuryContext.LoadEntireStorageAsync(cancel);

            treasuryContext.AddGuess(adding);
        }

        RemoveEmpty();
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
                    Copies = requests.Sum(r => r.Copies)
                })
            .ToList();
    }

    private void AttachCardRequests(IEnumerable<CardRequest> requests)
    {
        var detachedCards = requests
            .Select(cr => cr.Card)
            .Where(c => Entry(c).State is EntityState.Detached);

        // by default treats detached cards as existing
        // new vs existing cards should be handled outside of this function

        Cards.AttachRange(detachedCards);
    }

    public async Task ExchangeAsync(Deck deck, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(deck);

        if (Entry(deck).State is EntityState.Detached)
        {
            Decks.Attach(deck);
        }

        var storageSpaces = await StorageSpaces()
            .ToDictionaryAsync(s => (LocationIndex)s, cancel);

        string[] cardNames = deck.Wants
            .Select(w => w.Card.Name)
            .Union(deck.Givebacks
                .Select(g => g.Card.Name))
            .ToArray();

        await MatchingStorage(cardNames).LoadAsync(cancel);

        var treasuryContext = new TreasuryContext(this, storageSpaces);
        var exchangeContext = new ExchangeContext(this, treasuryContext);

        bool isFullyLoaded = false;

        exchangeContext.TakeExact();
        exchangeContext.TakeApproximate();

        exchangeContext.ReturnExact();
        exchangeContext.ReturnApproximate();

        if (deck.Givebacks.Any(g => g.Copies > 0))
        {
            await treasuryContext.LoadEntireStorageAsync(cancel);

            exchangeContext.ReturnGuess();

            isFullyLoaded = true;
        }

        if (treasuryContext.Available.Any())
        {
            if (!isFullyLoaded)
            {
                await treasuryContext.LoadEntireStorageAsync(cancel);
            }

            treasuryContext.ReduceExcessExact();
            treasuryContext.ReduceExcessApproximate();
        }

        RemoveEmpty();
    }

    public async Task UpdateBoxesAsync(CancellationToken cancel = default)
    {
        if (AreBoxesUnchanged() && AreHoldsUnchanged())
        {
            return;
        }

        var storageSpaces = await MergedStorageSpacesAsync(cancel);

        int[] modified = Boxes.Local
            .Where(b => Entry(b).State is EntityState.Modified)
            .Select(b => b.Id)
            .ToArray();

        await ArrangingStorage(modified).LoadAsync(cancel);

        var treasuryContext = new TreasuryContext(this, storageSpaces);

        treasuryContext.ReduceOverflowExact();
        treasuryContext.ReduceOverflowApproximate();

        treasuryContext.ReduceExcessExact();
        treasuryContext.ReduceExcessApproximate();

        RemoveEmpty();
    }

    private bool AreBoxesUnchanged()
    {
        return ChangeTracker
            .Entries<Box>()
            .All(e => e.State is EntityState.Detached
                || (e.State is not EntityState.Added
                    && !e.Property(b => b.Capacity).IsModified));
    }

    private bool AreHoldsUnchanged()
    {
        return ChangeTracker
            .Entries<Hold>()
            .All(e => e.State is EntityState.Detached
                || (e.State is not EntityState.Added
                    && !e.Property(h => h.Copies).IsModified));
    }

    private async Task<Dictionary<LocationIndex, StorageSpace>> MergedStorageSpacesAsync(CancellationToken cancel)
    {
        var storageSpaces = await StorageSpaces()
            .ToDictionaryAsync(s => (LocationIndex)s, cancel);

        var modifiedBoxes = Boxes.Local
            .Where(b => Entry(b).State is EntityState.Modified)
            .Join(storageSpaces,
                b => b.Id,
                kv => kv.Key.Id,
                (box, kv) => (kv.Key, kv.Value, box));

        foreach (var (index, space, box) in modifiedBoxes)
        {
            storageSpaces.Remove(index);

            storageSpaces.Add((LocationIndex)box, space with { Capacity = box.Capacity });
        }

        return storageSpaces;
    }

    #region Database Queries

    private IQueryable<StorageSpace> StorageSpaces()
    {
        return Locations
            .Where(l => l is Excess || l is Box)
            .OrderBy(l => l.Id)
            .Select(l => new StorageSpace
            {
                Id = l.Id,
                Name = l.Name,
                Held = l.Holds.Sum(h => h.Copies),
                Capacity = l is Box ? (l as Box)!.Capacity : null
            });
    }

    private IQueryable<Location> MatchingStorage(string[] cardNames)
    {
        return Locations
            .Where(l => l is Excess
                || (l is Box && l.Holds.Any(h => cardNames.Contains(h.Card.Name))))

            .Include(l => l.Holds
                .Where(h => cardNames.Contains(h.Card.Name))
                .OrderBy(h => h.Card.Name)
                    .ThenByDescending(h => h.Copies))
                .ThenInclude(h => h.Card)

            .OrderBy(l => l.Id);
    }

    private IQueryable<Location> ArrangingStorage(int[] modified)
    {
        // modified location values won't be overridden, only the unloaded info will be added

        return Locations
            .Where(l => modified.Contains(l.Id)
                || l is Excess
                || (l is Box
                    && (l as Box)!.Capacity < l.Holds.Sum(h => h.Copies)))

            .Include(l => l.Holds
                .OrderBy(h => h.Card.Name)
                    .ThenByDescending(h => h.Copies))
                .ThenInclude(h => h.Card)

            .OrderBy(l => l.Id);
    }

    #endregion

    private void RemoveEmpty()
    {
        var emptyHolds = Holds.Local
            .Where(h => h.Copies == 0);

        var emptyWants = Wants.Local
            .Where(w => w.Copies == 0);

        var emptyGivebacks = Givebacks.Local
            .Where(g => g.Copies == 0);

        var emptyTransactions = Transactions.Local
            .Where(t => !t.Changes.Any());

        Holds.RemoveRange(emptyHolds);
        Wants.RemoveRange(emptyWants);
        Givebacks.RemoveRange(emptyGivebacks);

        Transactions.RemoveRange(emptyTransactions);
    }
}
