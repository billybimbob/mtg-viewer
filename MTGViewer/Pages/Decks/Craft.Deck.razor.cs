using System.Collections.Generic;
using System.Linq;
using System.Paging;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;

namespace MTGViewer.Pages.Decks;

public partial class Craft
{
    private DeckCounts? _counts;
    private DeckContext? _deckContext;

    internal SeekList<Hold> DeckHolds { get; private set; } = SeekList<Hold>.Empty;
    internal SeekList<Giveback> DeckReturns { get; private set; } = SeekList<Giveback>.Empty;
    internal SeekList<Want> DeckWants { get; private set; } = SeekList<Want>.Empty;

    internal string DeckName =>
        _deckContext?.Deck.Name is string name && !string.IsNullOrWhiteSpace(name)
            ? name : "New Deck";

    internal EditContext? DeckEdit => _deckContext?.EditContext;

    internal int HeldCopies => (_counts?.HeldCopies ?? 0) - (_counts?.ReturnCopies ?? 0);
    internal int ReturnCopies => _counts?.ReturnCopies ?? 0;
    internal int WantCopies => _counts?.WantCopies ?? 0;

    internal bool CannotSave() => !_deckContext?.CanSave() ?? true;

    internal Deck? GetExchangeDeck()
    {
        if (_deckContext?.Deck is not Deck deck)
        {
            return null;
        }

        if (Result is SaveResult.Success)
        {
            return deck;
        }

        if (!_deckContext.CanSave()
            && (deck.Wants.Any(w => w.Copies > 0)
                || deck.Givebacks.Any(g => g.Copies > 0)))
        {
            return deck;
        }

        return null;
    }

    internal async Task ChangeHoldPageAsync(SeekRequest<Hold> request)
    {
        var newHolds = GetHolds(request);

        if (newHolds.Count == PageSize.Current)
        {
            DeckHolds = newHolds;
            return;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        await LoadQuantityPageAsync(dbContext, request);

        DeckHolds = GetHolds(request);
    }

    internal async Task ChangeHoldPageAsync(CardDbContext dbContext)
    {
        var firstRequest = new SeekRequest<Hold>();

        await LoadQuantityPageAsync(dbContext, firstRequest);

        DeckHolds = GetHolds(firstRequest);
    }

    private SeekList<Hold> GetHolds(SeekRequest<Hold> request)
    {
        if (_deckContext is null)
        {
            return SeekList<Hold>.Empty;
        }

        var holds = _deckContext
            .Groups
            .Where(g => g.Hold is not null)
            .Select(g => new Hold
            {
                CardId = g.CardId,
                Card = g.Card,

                LocationId = g.LocationId,
                Location = g.Location,

                Copies = (g.Hold?.Copies ?? 0) - (g.Giveback?.Copies ?? 0)
            });

        var orderedHolds = _cards
            .Join(holds,
                c => c.Id, h => h.Card.Id,
                (_, group) => group);

        return ToSeekList(orderedHolds, request, PageSize.Current + 1);
    }

    internal async Task ChangeReturnPageAsync(SeekRequest<Giveback> request)
    {
        var newReturns = GetReturns(request);

        if (newReturns.Count == PageSize.Current)
        {
            DeckReturns = newReturns;
            return;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        await LoadQuantityPageAsync(dbContext, request);

        DeckReturns = GetReturns(request);
    }

    internal async Task ChangeReturnPageAsync(CardDbContext dbContext)
    {
        var firstRequest = new SeekRequest<Giveback>();

        await LoadQuantityPageAsync(dbContext, firstRequest);

        DeckReturns = GetReturns(firstRequest);
    }

    private SeekList<Giveback> GetReturns(SeekRequest<Giveback> request)
    {
        if (_deckContext is null)
        {
            return SeekList<Giveback>.Empty;
        }

        var gives = _deckContext.Groups
            .Select(g => g.Giveback)
            .Where(g => g is { Copies: > 0 })
            .OfType<Giveback>();

        var orderedGives = _cards
            .Join(gives,
                c => c.Id, g => g.CardId,
                (_, giveback) => giveback);

        return ToSeekList(orderedGives, request, PageSize.Current + 1);
    }

    internal async Task ChangeWantPageAsync(SeekRequest<Want> request)
    {
        var newWants = GetWants(request);

        if (newWants.Count == PageSize.Current)
        {
            DeckWants = newWants;
            return;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        await LoadQuantityPageAsync(dbContext, request);

        DeckWants = GetWants(request);
    }

    internal async Task ChangeWantPageAsync(CardDbContext dbContext)
    {
        var firstRequest = new SeekRequest<Want>();

        await LoadQuantityPageAsync(dbContext, firstRequest);

        DeckWants = GetWants(firstRequest);
    }

    private SeekList<Want> GetWants(SeekRequest<Want> request)
    {
        if (_deckContext is null)
        {
            return SeekList<Want>.Empty;
        }

        var wants = _deckContext.Groups
            .Select(g => g.Want)
            .Where(w => w is { Copies: > 0 })
            .OfType<Want>();

        var orderedGives = _cards
            .Join(wants,
                c => c.Id, g => g.CardId,
                (_, giveback) => giveback);

        return ToSeekList(orderedGives, request, PageSize.Current + 1);
    }

    private static SeekList<TQuantity> ToSeekList<TQuantity>(
        IEnumerable<TQuantity> quantities,
        SeekRequest<TQuantity> request,
        int size)
        where TQuantity : Quantity
    {
        var items = request switch
        {
            (TQuantity o, SeekDirection.Backwards) => quantities
                .Where(t => (t.Card.Name, t.Card.SetName, t.Card.Id)
                    .CompareTo((o.Card.Name, o.Card.SetName, o.Card.Id)) < 0)
                .TakeLast(size)
                .ToList(),

            (TQuantity o, SeekDirection.Forward) => quantities
                .Where(t => (t.Card.Name, t.Card.SetName, t.Card.Id)
                    .CompareTo((o.Card.Name, o.Card.SetName, o.Card.Id)) > 0)
                .Take(size)
                .ToList(),

            (null, SeekDirection.Backwards) => quantities
                .TakeLast(size)
                .ToList(),

            (null, SeekDirection.Forward) or _ => quantities
                .Take(size)
                .ToList(),
        };

        bool lookAhead = items.Count == size;

        if (items.Any())
        {
            items.RemoveAt(items.Count - 1);
        }

        var seek = new Seek<TQuantity>(
            items,
            request.Direction,
            request.Seek is not null,
            lookAhead);

        return new SeekList<TQuantity>(seek, items);
    }

    private async Task LoadQuantityPageAsync<TQuantity>(CardDbContext dbContext, SeekRequest<TQuantity> request)
        where TQuantity : Quantity
    {
        if (_deckContext is not { Deck: Deck deck })
        {
            return;
        }

        dbContext.Cards.AttachRange(_cards);
        dbContext.Decks.Attach(deck); // attach for nav fixup

        var (seek, direction) = request;

        var newPage = DeckQuantitiesAsync(dbContext.Set<TQuantity>(), seek, direction);

        await foreach (var want in newPage.WithCancellation(_cancel.Token))
        {
            _deckContext.AddQuantity(want);
        }

        _cards.UnionWith(dbContext.Cards.Local);
    }

    private IAsyncEnumerable<TQuantity> DeckQuantitiesAsync<TQuantity>(
        DbSet<TQuantity> db,
        TQuantity? reference,
        SeekDirection direction) where TQuantity : Quantity
    {
        return db
            .Where(q => q.LocationId == DeckId)
            .Include(q => q.Card)

            .OrderBy(q => q.Card.Name)
                .ThenBy(q => q.Card.SetName)
                .ThenBy(q => q.CardId)

            .SeekOrigin(reference, direction)
            .Take(PageSize.Current + 1)

            .AsAsyncEnumerable();
    }

    internal async Task AddWantAsync(Card card)
    {
        if (_counts is null)
        {
            return;
        }

        if (BuildOption is not BuildType.Theorycrafting)
        {
            Logger.LogWarning("Build type is not expected {Expected}", BuildType.Theorycrafting);
            return;
        }

        var want = await FindWantAsync(card);

        if (want is null)
        {
            Logger.LogWarning("Could not find want for {CardName}", card.Name);
            return;
        }

        if (want.Copies >= PageSize.Limit)
        {
            Logger.LogWarning("Deck want failed since at limit");
            return;
        }

        if (want.Copies == 0)
        {
            // implies that just added
            _counts.WantCount += 1;
        }

        _counts.WantCopies += 1;

        want.Copies += 1;

        Result = SaveResult.None;
    }

    private async Task<Want?> FindWantAsync(Card card)
    {
        if (_deckContext is not { Deck: Deck deck })
        {
            return null;
        }

        if (_deckContext.TryGetQuantity(card, out Want localWant))
        {
            return localWant;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        dbContext.Cards.Attach(card);
        dbContext.Decks.Attach(deck);

        var want = await dbContext
            .Wants
            .SingleOrDefaultAsync(w =>
                w.LocationId == deck.Id && w.CardId == card.Id, _cancel.Token)

            ?? dbContext
                .Wants
                .Attach(new Want { Card = card, Location = deck })
                .Entity;

        _deckContext.AddQuantity(want);

        return want;
    }

    internal void RemoveWant(Card card)
    {
        if (_deckContext is null || _counts is null)
        {
            return;
        }

        if (BuildOption is not BuildType.Theorycrafting)
        {
            Logger.LogWarning("Build type is not expected {Expected}", BuildType.Theorycrafting);
            return;
        }

        // assumption that want will always be loaded for the want to remove
        // the only way to trigger this handler is clicking a loaded want

        if (!_deckContext.TryGetQuantity(card, out Want want) || want.Copies <= 0)
        {
            Logger.LogWarning("{CardName} is missing the required wants to remove", card.Name);
            return;
        }

        _counts.WantCopies -= 1;

        want.Copies -= 1;

        if (want.Copies == 0)
        {
            _counts.WantCount -= 1;
        }

        Result = SaveResult.None;
    }

    internal async Task AddReturnAsync(Card card)
    {
        if (_deckContext is null || _counts is null)
        {
            return;
        }

        if (BuildOption is not BuildType.Holds)
        {
            Logger.LogWarning("Build type is not expected {Expected}", BuildType.Holds);
            return;
        }

        var deckReturn = await FindReturnAsync(card);

        if (deckReturn is not var (hold, giveback))
        {
            Logger.LogWarning("Missing required Hold for Card {CardName}", card.Name);
            return;
        }

        int remaining = hold.Copies - giveback.Copies;

        if (remaining == 0)
        {
            Logger.LogError("There are no more of {CardName} to remove", card.Name);
            return;
        }

        if (giveback.Copies == 0)
        {
            _counts.ReturnCount += 1; // implies that just added
        }

        _counts.ReturnCopies += 1;

        giveback.Copies += 1;

        Result = SaveResult.None;
    }

    private async Task<(Hold, Giveback)?> FindReturnAsync(Card card)
    {
        if (_deckContext is not { Deck: Deck deck })
        {
            return null;
        }

        bool holdExists = _deckContext.TryGetQuantity(card, out Hold hold);
        bool giveExists = _deckContext.TryGetQuantity(card, out Giveback give);

        if (holdExists && giveExists)
        {
            return (hold, give);
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        dbContext.Cards.Attach(card);
        dbContext.Decks.Attach(deck);

        if (!holdExists)
        {
            var dbHold = await dbContext.Holds
                .SingleOrDefaultAsync(h =>
                    h.LocationId == deck.Id && h.CardId == card.Id, _cancel.Token);

            if (dbHold is null)
            {
                return null;
            }

            _deckContext.AddQuantity(dbHold);

            hold = dbHold;
        }

        if (!giveExists)
        {
            give = await dbContext
                .Givebacks
                .SingleOrDefaultAsync(
                    g => g.LocationId == deck.Id && g.CardId == card.Id, _cancel.Token)

                ?? dbContext
                    .Givebacks
                    .Attach(new Giveback { Location = deck, Card = card })
                    .Entity;

            _deckContext.AddQuantity(give);
        }

        return (hold, give);
    }

    internal void RemoveReturn(Card card)
    {
        if (_deckContext is null || _counts is null)
        {
            return;
        }

        if (BuildOption is not BuildType.Holds)
        {
            Logger.LogWarning("Build type is not expected {Expected}", BuildType.Holds);
            return;
        }

        // assumption that want will always be loaded for the giveback to remove
        // the only way to trigger this handler is clicking a loaded giveback

        if (!_deckContext.TryGetQuantity(card, out Giveback giveback) || giveback.Copies <= 0)
        {
            Logger.LogWarning("{CardName} is missing the required wants to remove", card.Name);
            return;
        }

        _counts.ReturnCopies -= 1;

        giveback.Copies -= 1;

        if (giveback.Copies == 0)
        {
            _counts.ReturnCount -= 1;
        }

        Result = SaveResult.None;
    }
}
