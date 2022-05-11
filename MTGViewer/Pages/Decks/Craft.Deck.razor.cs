using System;
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

    private LoadedSeekList<Hold> _holds;
    private LoadedSeekList<Giveback> _givebacks;
    private LoadedSeekList<Want> _wants;

    internal string DeckName => _deckContext?.Deck.Name ?? "New Deck";
    internal EditContext? DeckEdit => _deckContext?.EditContext;

    internal int HeldCopies => (_counts?.HeldCopies ?? 0) - (_counts?.ReturnCopies ?? 0);
    internal int ReturnCopies => _counts?.ReturnCopies ?? 0;
    internal int WantCopies => _counts?.WantCopies ?? 0;

    internal SeekList<Hold> DeckHolds => _holds.List ?? SeekList<Hold>.Empty;
    internal SeekList<Giveback> DeckReturns => _givebacks.List ?? SeekList<Giveback>.Empty;
    internal SeekList<Want> DeckWants => _wants.List ?? SeekList<Want>.Empty;

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

    private void InitializeHolds()
    {
        if (_holds == default)
        {
            _holds = GetQuantities(_holds.Request, includeEmpty: true);
        }
    }

    private void InitializeReturns()
    {
        if (_givebacks == default)
        {
            _givebacks = GetQuantities(_givebacks.Request);
        }
    }

    private async Task InitializeHoldsAsync(CardDbContext dbContext)
    {
        if (_holds == default)
        {
            await LoadQuantitiesAsync(dbContext, _holds.Request);

            _holds = GetQuantities(_holds.Request, includeEmpty: true);
        }
    }

    private async Task InitializeReturnsAsync(CardDbContext dbContext)
    {
        if (_givebacks == default)
        {
            await LoadQuantitiesAsync(dbContext, _givebacks.Request);

            _givebacks = GetQuantities(_givebacks.Request);
        }
    }

    private async Task InitializeWantsAsync(CardDbContext dbContext)
    {
        if (_wants == default)
        {
            await LoadQuantitiesAsync(dbContext, _wants.Request);

            _wants = GetQuantities(_wants.Request);
        }
    }

    internal async Task ChangeHoldsAsync(SeekRequest<Hold> request)
    {
        var newHolds = GetQuantities(request, includeEmpty: true);

        if (newHolds.List?.Count == PageSize.Current - 1)
        {
            _holds = newHolds;
            return;
        }

        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await LoadQuantitiesAsync(dbContext, request);

            _holds = GetQuantities(request, includeEmpty: true);
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

    internal async Task ChangeReturnsAsync(SeekRequest<Giveback> request)
    {
        var newReturns = GetQuantities(request);

        if (newReturns.List?.Count == PageSize.Current - 1)
        {
            _givebacks = newReturns;
            return;
        }

        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await LoadQuantitiesAsync(dbContext, request);

            _givebacks = GetQuantities(request);
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

    internal async Task ChangeWantsAsync(SeekRequest<Want> request)
    {
        var newWants = GetQuantities(request);

        if (newWants.List?.Count == PageSize.Current - 1)
        {
            _wants = newWants;
            return;
        }

        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await LoadQuantitiesAsync(dbContext, request);

            _wants = GetQuantities(request);
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

    private LoadedSeekList<TQuantity> GetQuantities<TQuantity>(
        SeekRequest<TQuantity> request,
        bool includeEmpty = false)
        where TQuantity : Quantity
    {
        if (_deckContext is null)
        {
            return default;
        }

        var quantities = _deckContext.Groups
            .Select(g => g.GetQuantity<TQuantity>())
            .OfType<TQuantity>()
            .Where(q => includeEmpty || q.Copies > 0);

        var orderedQuantities = _cards
            .Join(quantities,
                c => c.Id,
                q => q.CardId,
                (_, quantity) => quantity);

        return ToSeekList(orderedQuantities, request, PageSize.Current);
    }

    private static LoadedSeekList<TQuantity> ToSeekList<TQuantity>(
        IEnumerable<TQuantity> quantities,
        SeekRequest<TQuantity> request,
        int size)
        where TQuantity : Quantity
    {
        var items = request switch
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

        bool lookAhead = items.Count == size;
        if (lookAhead)
        {
            items.RemoveAt(items.Count - 1);
        }

        var direction = request.Direction;
        bool hasOrigin = request.Seek is not null;

        var seek = new Seek<TQuantity>(items, direction, hasOrigin, lookAhead);
        var list = new SeekList<TQuantity>(seek, items);

        return new LoadedSeekList<TQuantity>(request, list);
    }

    private async Task LoadQuantitiesAsync<TQuantity>(CardDbContext dbContext, SeekRequest<TQuantity> request)
        where TQuantity : Quantity
    {
        if (_deckContext is not { Deck: Deck deck })
        {
            Logger.LogWarning("Deck is missing");
            return;
        }

        dbContext.Cards.AttachRange(_cards);
        dbContext.Decks.Attach(deck); // attach for nav fixup

        var (seek, direction) = request;

        var newPage = DeckQuantitiesAsync(dbContext, deck.Id, seek, direction, PageSize.Current);

        await foreach (var quantity in newPage.WithCancellation(_cancel.Token))
        {
            _deckContext.AddOriginalQuantity(quantity);
        }

        _cards.UnionWith(dbContext.Cards.Local);
    }

    private static IAsyncEnumerable<TQuantity> DeckQuantitiesAsync<TQuantity>(
        CardDbContext dbContext,
        int deckId,
        TQuantity? reference,
        SeekDirection direction,
        int size)
        where TQuantity : Quantity
    {
        return dbContext
            .Set<TQuantity>()
            .Where(q => q.LocationId == deckId)
            .Include(q => q.Card)

            .OrderBy(q => q.Card.Name)
                .ThenBy(q => q.Card.SetName)
                .ThenBy(q => q.CardId)

            .SeekOrigin(reference, direction)
            .Take(size)

            .AsAsyncEnumerable();
    }

    #region Edit Quantities

    internal async Task AddWantAsync(Card card)
    {
        if (BuildOption is not BuildType.Theorycrafting)
        {
            Logger.LogWarning("Build type is not expected {Expected}", BuildType.Theorycrafting);
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

    private void AddWant(Want? want)
    {
        if (want is null || _counts is null)
        {
            return;
        }

        want.Copies += 1;

        if (want.Copies == 1)
        {
            _wants = GetQuantities(_wants.Request);
            _counts.WantCount += 1;
        }

        _counts.WantCopies += 1;

        Result = SaveResult.None;
    }

    internal void RemoveWant(Card card)
    {
        if (BuildOption is not BuildType.Theorycrafting)
        {
            Logger.LogWarning("Build type is not expected {Expected}", BuildType.Theorycrafting);
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
            _wants = GetQuantities(_wants.Request);
            _counts.WantCount -= 1;
        }

        _counts.WantCopies -= 1;

        Result = SaveResult.None;
    }

    internal async Task AddReturnAsync(Card card)
    {
        if (BuildOption is not BuildType.Holds)
        {
            Logger.LogWarning("Build type is not expected {Expected}", BuildType.Holds);
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
        if (_deckContext is not { Deck: Deck deck })
        {
            return null;
        }

        bool holdExists = _deckContext.TryGetQuantity(card, out Hold hold);
        bool giveExists = _deckContext.TryGetQuantity(card, out Giveback give);

        if (holdExists && giveExists)
        {
            return give;
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
                Logger.LogWarning("Missing required Hold for Card {CardName}", card.Name);
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

        int remaining = hold.Copies - give.Copies;

        if (remaining == 0)
        {
            Logger.LogError("There are no more of {CardName} to remove", card.Name);
            return null;
        }

        return give;
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
            _givebacks = GetQuantities(_givebacks.Request);
            _counts.ReturnCount += 1;
        }

        _counts.ReturnCopies += 1;

        Result = SaveResult.None;
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

        giveback.Copies -= 1;

        if (giveback.Copies == 0)
        {
            _givebacks = GetQuantities(_givebacks.Request);
            _counts.ReturnCount -= 1;
        }

        _counts.ReturnCopies -= 1;

        Result = SaveResult.None;
    }

    #endregion
}
