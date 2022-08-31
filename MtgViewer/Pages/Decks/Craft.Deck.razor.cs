using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Decks;

public partial class Craft
{
    private SeekRequest<Want> _lastWant;
    private SeekRequest<Giveback> _lastReturn;

    private DeckCounts? _counts;
    private DeckContext? _deckContext;

    #region View Properties

    internal string DeckName => _deckContext?.Deck.Name ?? "New Deck";

    internal EditContext? DeckEdit => _deckContext?.EditContext;

    internal int HeldCopies => (_counts?.HeldCopies ?? 0) - (_counts?.ReturnCopies ?? 0);

    internal int ReturnCopies => _counts?.ReturnCopies ?? 0;

    internal int WantCopies => _counts?.WantCopies ?? 0;

    internal SeekList<Hold> DeckHolds { get; private set; } = SeekList.Empty<Hold>();

    internal SeekList<Giveback> DeckReturns { get; private set; } = SeekList.Empty<Giveback>();

    internal SeekList<Want> DeckWants { get; private set; } = SeekList.Empty<Want>();

    #endregion

    internal bool CannotSave()
        => !_deckContext?.CanSave() ?? true;

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

    private async Task CreateDeckOrRedirectAsync(CardDbContext dbContext, string? userId)
    {
        if (userId is null)
        {
            Nav.NavigateTo("/Decks", forceLoad: true, replace: true);
            return;
        }

        bool userExists = await dbContext.Players
            .AnyAsync(p => p.Id == userId, _cancel.Token);

        if (!userExists)
        {
            Nav.NavigateTo("/Decks", forceLoad: true, replace: true);
            return;
        }

        int userDeckCount = await dbContext.Decks
            .CountAsync(d => d.OwnerId == userId, _cancel.Token);

        var newDeck = new Deck
        {
            Name = $"Deck #{userDeckCount + 1}",
            OwnerId = userId
        };

        _counts = new DeckCounts { OwnerId = userId };

        _deckContext = new DeckContext(newDeck);
    }

    private async Task FetchDeckOrRedirectAsync(CardDbContext dbContext, string? userId)
    {
        if (userId is null)
        {
            Nav.NavigateTo("/Decks", forceLoad: true, replace: true);
            return;
        }

        var counts = await DeckCountsAsync.Invoke(dbContext, DeckId, _cancel.Token);

        if (counts is null || counts.OwnerId != userId || counts.HasTrades)
        {
            Nav.NavigateTo(
                Nav.GetUriWithQueryParameter(nameof(DeckId), null as int?), replace: true);
            return;
        }

        var deck = await dbContext.Decks
            .SingleOrDefaultAsync(d => d.Id == DeckId, _cancel.Token);

        if (deck is null)
        {
            Nav.NavigateTo(
                Nav.GetUriWithQueryParameter(nameof(DeckId), null as int?), replace: true);
            return;
        }

        _counts = counts;

        _deckContext = new DeckContext(deck);
    }

    private static readonly Func<CardDbContext, int, CancellationToken, Task<DeckCounts?>> DeckCountsAsync
        = EF.CompileAsyncQuery((CardDbContext db, int id, CancellationToken _)
            => db.Decks
                .Where(d => d.Id == id)
                .Select(d => new DeckCounts
                {
                    Id = d.Id,
                    OwnerId = d.OwnerId,

                    HeldCopies = d.Holds.Sum(h => h.Copies),
                    WantCopies = d.Wants.Sum(w => w.Copies),
                    ReturnCopies = d.Givebacks.Sum(g => g.Copies),

                    HeldCount = d.Holds.Count,
                    WantCount = d.Wants.Count,
                    ReturnCount = d.Givebacks.Count,

                    HasTrades = d.TradesTo.Any()
                })
                .SingleOrDefault());

    internal async Task ChangeQuantitiesAsync<TQuantity>(SeekRequest<TQuantity> request)
        where TQuantity : Quantity
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            dbContext.Cards.AttachRange(_cards);

            var (origin, direction) = request;

            switch (await FetchQuantitiesAsync(dbContext, origin, direction))
            {
                case SeekList<Hold> hs:
                    DeckHolds = hs;
                    break;

                case SeekList<Want> ws:
                    DeckWants = ws;
                    break;

                case SeekList<Giveback> gs:
                    DeckReturns = gs;
                    break;

                default:
                    break;
            }

            _cards.UnionWith(dbContext.Cards.Local);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Warning}", ex);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task LoadQuantitiesAsync<TQuantity>(
        CardDbContext dbContext,
        TQuantity? origin,
        SeekDirection direction)
        where TQuantity : Quantity
    {
        if (_deckContext is not { Deck: Deck deck })
        {
            Logger.LogWarning("Deck is missing");
            return;
        }

        if (_deckContext.IsNewDeck)
        {
            // no quantities will exist in the db
            return;
        }

        dbContext.Decks.Attach(deck); // attach for nav fixup
        dbContext.Cards.AttachRange(_cards);

        var dbQuantities = DbQuantitiesAsync(dbContext, deck.Id, origin, direction);

        await foreach (var quantity in dbQuantities.WithCancellation(_cancel.Token))
        {
            if (!_deckContext.TryGetQuantity(quantity.Card, out TQuantity _))
            {
                _deckContext.AddOriginalQuantity(quantity);
            }

            if (!_cards.Contains(quantity.Card))
            {
                _ = _cards.Add(quantity.Card);
            }
        }
    }

    private IAsyncEnumerable<TQuantity> DbQuantitiesAsync<TQuantity>(
        CardDbContext dbContext,
        int deckId,
        TQuantity? origin,
        SeekDirection direction)
        where TQuantity : Quantity
    {
        var quantities = dbContext
            .Set<TQuantity>()
            .Where(q => q.LocationId == deckId);

        string? name = Filter.Name?.ToUpperInvariant();
        string? text = Filter.Text?.ToUpperInvariant();

        string[] types = Filter.Types?.ToUpperInvariant().Split() ?? Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(name))
        {
            quantities = quantities
                .Where(q => q.Card.Name.ToUpper().Contains(name));
        }

        if (Filter.Mana is ManaFilter mana)
        {
            quantities = quantities.Where(mana.CreateFilter<TQuantity>());
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            quantities = quantities
                .Where(q => q.Card.Text != null
                    && q.Card.Text.ToUpper().Contains(text));
        }

        foreach (string type in types)
        {
            quantities = quantities
                .Where(q => q.Card.Type.ToUpper().Contains(type));
        }

        if (PickedColors is not Color.None)
        {
            quantities = quantities
                .Where(q => q.Card.Color.HasFlag(PickedColors));
        }

        return quantities
            .Include(q => q.Card)

            .OrderBy(q => q.Card.Name)
                .ThenBy(q => q.Card.SetName)
                .ThenBy(q => q.CardId)

            .SeekBy(direction)
                .After(origin)
                .ThenTake(PageSize.Current)

            .AsAsyncEnumerable();
    }

    private SeekList<TQuantity> SeekQuantities<TQuantity>(
        TQuantity? origin,
        SeekDirection direction)
        where TQuantity : Quantity
    {
        var quantities = FilterQuantities<TQuantity>();

        if (quantities is null)
        {
            return SeekList.Empty<TQuantity>();
        }

        int size = PageSize.Current;

        var items = (origin, direction) switch
        {
            (TQuantity o, SeekDirection.Backwards) => quantities
                .Where(t => (t.Card.Name, t.Card.SetName, t.CardId)
                    .CompareTo((o.Card.Name, o.Card.SetName, o.CardId)) < 0)
                .TakeLast(size)
                .ToList(),

            (TQuantity o, SeekDirection.Forward) => quantities
                .Where(t => (t.Card.Name, t.Card.SetName, t.CardId)
                    .CompareTo((o.Card.Name, o.Card.SetName, o.CardId)) > 0)
                .Take(size)
                .ToList(),

            (null, SeekDirection.Backwards) => quantities
                .TakeLast(size)
                .ToList(),

            (null, SeekDirection.Forward) or _ => quantities
                .Take(size)
                .ToList(),
        };

        bool hasOrigin = origin is not null;
        bool lookAhead = items.Count == size;
        int targetSize = size - 1;

        if (lookAhead && direction is SeekDirection.Forward)
        {
            items.RemoveAt(items.Count - 1);
        }
        else if (lookAhead && direction is SeekDirection.Backwards)
        {
            items.RemoveAt(0);
        }

        return new SeekList<TQuantity>(items, direction, hasOrigin, lookAhead, targetSize);
    }

    private IEnumerable<TQuantity>? FilterQuantities<TQuantity>()
        where TQuantity : Quantity
    {
        if (_deckContext is null)
        {
            return null;
        }

        var quantities = _deckContext.Groups
            .Select(g => g.GetQuantity<TQuantity>())
            .Where(q => q is Hold or { Copies: > 0 })
            .Cast<TQuantity>();

        string? name = Filter.Name?.ToUpperInvariant();
        string? text = Filter.Text?.ToUpperInvariant();

        string[] types = Filter.Types?.ToUpperInvariant().Split() ?? Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(name))
        {
            quantities = quantities
                .Where(q => q.Card.Name.ToUpperInvariant().Contains(name));
        }

        if (Filter.Mana is ManaFilter mana)
        {
            quantities = quantities.Where(mana.CreateFilter<TQuantity>().Compile());
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            quantities = quantities
                .Where(q => q.Card.Text != null
                    && q.Card.Text.ToUpperInvariant().Contains(text));
        }

        foreach (string type in types)
        {
            quantities = quantities
                .Where(q => q.Card.Type.ToUpperInvariant().Contains(type));
        }

        if (PickedColors is not Color.None)
        {
            quantities = quantities
                .Where(q => q.Card.Color.HasFlag(PickedColors));
        }

        return _cards
            .Join(quantities,
                c => c.Id,
                q => q.CardId,
                (_, quantity) => quantity);
    }

    private async Task<SeekList<TQuantity>> FetchQuantitiesAsync<TQuantity>(
        CardDbContext dbContext,
        TQuantity? origin,
        SeekDirection direction)
        where TQuantity : Quantity
    {
        await LoadQuantitiesAsync(dbContext, origin, direction);

        switch (origin)
        {
            case Want w:
                _lastWant = new SeekRequest<Want>(w, direction);
                break;

            case Giveback g:
                _lastReturn = new SeekRequest<Giveback>(g, direction);
                break;

            default:
                break;
        }

        return SeekQuantities(origin, direction);
    }

    #region Edit Quantities

    internal async Task AddWantAsync(Card card)
    {
        if (DeckCraft is not DeckCraft.Theorycraft)
        {
            Logger.LogWarning("Build type is not expected {Expected}", DeckCraft.Theorycraft);
            return;
        }

        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            AddWant(await FindWantAsync(card));
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Warning}", ex);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task<Want?> FindWantAsync(Card card)
    {
        if (_deckContext is not { Deck: Deck deck })
        {
            return null;
        }

        if (_deckContext.TryGetQuantity(card, out Want local))
        {
            return local;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        dbContext.Cards.Attach(card);

        if (_deckContext.IsNewDeck)
        {
            dbContext.Decks.Add(deck);
        }
        else
        {
            dbContext.Decks.Attach(deck);
        }

        var want = await dbContext.Wants
            .SingleOrDefaultAsync(w =>
                w.LocationId == deck.Id && w.CardId == card.Id, _cancel.Token);

        if (want is null)
        {
            want = dbContext.Wants
                .Attach(new Want { Location = deck, Card = card })
                .Entity;

            _deckContext.AddQuantity(want);
        }
        else
        {
            _deckContext.AddOriginalQuantity(want);
        }

        return want;
    }

    private void AddWant(Want? want)
    {
        if (want is null || _counts is null)
        {
            return;
        }

        want.Copies += 1;

        if (want.Copies == 1)
        {
            var (origin, direction) = _lastWant;

            DeckWants = SeekQuantities(origin, direction);

            _counts.WantCount += 1;
        }

        _counts.WantCopies += 1;

        Result = SaveResult.None;
    }

    internal async Task RemoveWantAsync(Card card)
    {
        if (DeckCraft is not DeckCraft.Theorycraft)
        {
            Logger.LogWarning("Build type is not expected {Expected}", DeckCraft.Theorycraft);
            return;
        }

        if (_deckContext is null || _counts is null)
        {
            return;
        }

        // assumption that want will always be loaded for the want to remove
        // the only way to trigger this handler is clicking a loaded want

        if (!_deckContext.TryGetQuantity(card, out Want want) || want.Copies <= 0)
        {
            Logger.LogWarning("{CardName} is missing the required wants to remove", card.Name);
            return;
        }

        want.Copies -= 1;

        if (want.Copies == 0)
        {
            await LoadAdjacentPagesAsync(DeckWants.Seek);

            var (origin, direction) = _lastWant;

            DeckWants = SeekQuantities(origin, direction);

            _counts.WantCount -= 1;
        }

        _counts.WantCopies -= 1;

        Result = SaveResult.None;
    }

    internal async Task AddReturnAsync(Card card)
    {
        if (DeckCraft is not DeckCraft.Built)
        {
            Logger.LogWarning("Build type is not expected {Expected}", DeckCraft.Built);
            return;
        }

        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            AddReturn(await FindReturnAsync(card));
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Warning}", ex);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task<Giveback?> FindReturnAsync(Card card)
    {
        var hold = await FindHoldAsync(card);

        if (hold is null)
        {
            Logger.LogWarning("Missing required Hold for Card {CardName}", card.Name);
            return null;
        }

        var giveback = await FindGivebackAsync(card);

        if (giveback is null)
        {
            Logger.LogError("Giveback for {Card} is missing", card.Name);
            return null;
        }

        if (hold.Copies - giveback.Copies <= 0)
        {
            Logger.LogError("There are no more of {CardName} to remove", card.Name);
            return null;
        }

        return giveback;
    }

    private async Task<Hold?> FindHoldAsync(Card card)
    {
        if (_deckContext is not { Deck: Deck deck })
        {
            return null;
        }

        if (_deckContext.TryGetQuantity(card, out Hold local))
        {
            return local;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        dbContext.Cards.Attach(card);

        if (_deckContext.IsNewDeck)
        {
            dbContext.Decks.Add(deck);
        }
        else
        {
            dbContext.Decks.Attach(deck);
        }

        var hold = await dbContext.Holds
            .SingleOrDefaultAsync(h =>
                h.LocationId == deck.Id && h.CardId == card.Id, _cancel.Token);

        if (hold is null)
        {
            return null;
        }

        _deckContext.AddOriginalQuantity(hold);

        return hold;
    }

    private async Task<Giveback?> FindGivebackAsync(Card card)
    {
        if (_deckContext is not { Deck: Deck deck })
        {
            return null;
        }

        if (_deckContext.TryGetQuantity(card, out Giveback local))
        {
            return local;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        dbContext.Cards.Attach(card);

        if (_deckContext.IsNewDeck)
        {
            dbContext.Decks.Add(deck);
        }
        else
        {
            dbContext.Decks.Attach(deck);
        }

        var giveback = await dbContext.Givebacks
            .SingleOrDefaultAsync(g =>
                g.LocationId == deck.Id && g.CardId == card.Id, _cancel.Token);

        if (giveback is null)
        {
            giveback = dbContext.Givebacks
                .Attach(new Giveback { Location = deck, Card = card })
                .Entity;

            _deckContext.AddQuantity(giveback);
        }
        else
        {
            _deckContext.AddOriginalQuantity(giveback);
        }

        return giveback;
    }

    private void AddReturn(Giveback? giveback)
    {
        if (giveback is null || _counts is null)
        {
            return;
        }

        giveback.Copies += 1;

        if (giveback.Copies == 1)
        {
            var (origin, direction) = _lastReturn;

            DeckReturns = SeekQuantities(origin, direction);

            _counts.ReturnCount += 1;
        }

        _counts.ReturnCopies += 1;

        Result = SaveResult.None;
    }

    internal async Task RemoveReturnAsync(Card card)
    {
        if (_deckContext is null || _counts is null)
        {
            return;
        }

        if (DeckCraft is not DeckCraft.Built)
        {
            Logger.LogWarning("Build type is not expected {Expected}", DeckCraft.Built);
            return;
        }

        // assumption that want will always be loaded for the giveback to remove
        // the only way to trigger this handler is clicking a loaded giveback

        if (!_deckContext.TryGetQuantity(card, out Giveback giveback) || giveback.Copies <= 0)
        {
            Logger.LogWarning("{CardName} is missing the required wants to remove", card.Name);
            return;
        }

        giveback.Copies -= 1;

        if (giveback.Copies == 0)
        {
            await LoadAdjacentPagesAsync(DeckReturns.Seek);

            var (origin, direction) = _lastReturn;

            DeckReturns = SeekQuantities(origin, direction);

            _counts.ReturnCount -= 1;
        }

        _counts.ReturnCopies -= 1;

        Result = SaveResult.None;
    }

    private async Task LoadAdjacentPagesAsync<TQuantity>(Seek<TQuantity> seek)
        where TQuantity : Quantity
    {
        bool hasNoAdjacent = seek.Previous is null && seek.Next is null;

        if (hasNoAdjacent)
        {
            return;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        dbContext.Cards.AttachRange(_cards);

        if (seek.Previous is TQuantity previous)
        {
            await LoadQuantitiesAsync(dbContext, previous, SeekDirection.Backwards);
        }

        if (seek.Next is TQuantity next)
        {
            await LoadQuantitiesAsync(dbContext, next, SeekDirection.Forward);
        }

        _cards.UnionWith(dbContext.Cards.Local);
    }

    #endregion
}
