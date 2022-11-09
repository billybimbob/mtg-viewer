using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;
using MtgViewer.Services;
using MtgViewer.Utils;

namespace MtgViewer.Pages.Decks;

[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public partial class Craft : OwningComponentBase
{
    [Parameter]
    public int DeckId { get; set; }

    [CascadingParameter]
    public required Task<AuthenticationState> AuthState { get; set; }

    [Inject]
    public required IDbContextFactory<CardDbContext> DbFactory { get; set; }

    [Inject]
    public required PersistentComponentState ApplicationState { get; set; }

    [Inject]
    public required ParseTextFilter ParseTextFilter { get; set; }

    [Inject]
    public required PageSize PageSize { get; set; }

    [Inject]
    public required NavigationManager Nav { get; set; }

    [Inject]
    public required ILogger<Craft> Logger { get; set; }

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal DeckCraft DeckCraft { get; private set; }

    internal SaveResult Result { get; set; }

    private readonly CancellationTokenSource _cancel = new();
    private readonly SortedSet<Card> _cards = new(CardNameComparer.Instance);

    private PersistingComponentStateSubscription _persistSubscription;

    private bool _isBusy;
    private bool _isInteractive;

    protected override void OnInitialized() =>
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistCardDataAsync);

    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            await LoadDeckDataAsync();
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Warning}", ex);
        }
        catch (NavigationException ex)
        {
            Logger.LogWarning("Navigation {Warning}", ex);
        }
        finally
        {
            _isBusy = false;
        }
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            _isInteractive = true;

            StateHasChanged();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _persistSubscription.Dispose();

            _cancel.Cancel();
            _cancel.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task PersistCardDataAsync()
    {
        if (_deckContext is null)
        {
            return;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        var deckData = new DeckDto(dbContext, _deckContext);

        ApplicationState.PersistAsJson(nameof(_deckContext), deckData);
        ApplicationState.PersistAsJson(nameof(_counts), _counts);
        ApplicationState.PersistAsJson(nameof(_cards), _cards);
    }

    private async Task LoadDeckDataAsync()
    {
        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        if (TryPersistedLoad(dbContext))
        {
            return;
        }

        string? userId = await GetUserIdAsync();

        if (DeckId == default)
        {
            await CreateDeckOrRedirectAsync(dbContext, userId);
        }
        else
        {
            await FetchDeckOrRedirectAsync(dbContext, userId);
        }

        DeckHolds = await FetchQuantitiesAsync(dbContext, null as Hold, SeekDirection.Forward);
        DeckReturns = await FetchQuantitiesAsync(dbContext, null as Giveback, SeekDirection.Forward);
    }

    private bool TryPersistedLoad(CardDbContext dbContext)
    {
        if (!ApplicationState.TryGetData(nameof(_counts), out DeckCounts? counts)
            || !ApplicationState.TryGetData(nameof(_deckContext), out DeckDto? deckData)
            || !ApplicationState.TryGetData(nameof(_cards), out IReadOnlyCollection<Card>? cards))
        {
            return false;
        }

        _counts = counts;
        _deckContext = deckData.ToDeckContext(dbContext);

        _cards.UnionWith(cards);

        if (_deckContext.IsNewDeck)
        {
            dbContext.Decks.Add(_deckContext.Deck);
        }
        else
        {
            dbContext.Decks.Attach(_deckContext.Deck);
        }

        dbContext.Cards.AttachRange(cards);

        DeckHolds = SeekQuantities(null as Hold, SeekDirection.Forward);
        DeckReturns = SeekQuantities(null as Giveback, SeekDirection.Forward);

        return true;
    }

    private async ValueTask<string?> GetUserIdAsync()
    {
        var authState = await AuthState;

        _cancel.Token.ThrowIfCancellationRequested();

        var userManager = ScopedServices.GetRequiredService<UserManager<CardUser>>();

        string? userId = userManager.GetUserId(authState.User);

        if (userId is null)
        {
            Logger.LogWarning("User {User} is missing", authState.User);
            return null;
        }

        return userId;
    }

    internal async Task UpdateDeckCraftAsync(ChangeEventArgs args)
    {
        if (_isBusy || !Enum.TryParse(args.Value?.ToString(), out DeckCraft value))
        {
            return;
        }

        _isBusy = true;

        try
        {
            DeckCraft = value;

            await ApplyFiltersAsync();
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

    #region Save Data

    internal async Task CommitChangesAsync()
    {
        if (_isBusy
            || _deckContext is null
            || !_deckContext.CanSave())
        {
            return;
        }

        _isBusy = true;

        try
        {
            Result = await SaveOrConcurrentRecoverAsync();

            if (_deckContext.IsNewDeck)
            {
                Nav.NavigateTo(
                    Nav.GetUriWithQueryParameter(nameof(DeckId), _deckContext.Deck.Id));
            }
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);

            Result = SaveResult.Error;
        }
        catch (DbUpdateException ex)
        {
            Logger.LogError("{Error}", ex);

            Result = SaveResult.Error;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task<SaveResult> SaveOrConcurrentRecoverAsync()
    {
        if (_deckContext is null)
        {
            return SaveResult.Error;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        try
        {
            PrepareChanges(dbContext, _deckContext, _cards);

            await dbContext.SaveChangesAsync(_cancel.Token);

            _deckContext.SuccessfullySaved();

            return SaveResult.Success;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await UpdateDeckFromDbAsync(dbContext, ex);

            return SaveResult.Error;
        }
    }

    private static void PrepareChanges(CardDbContext dbContext, DeckContext deckContext, ICollection<Card> cards)
    {
        var deck = deckContext.Deck;

        if (deckContext.IsNewDeck)
        {
            dbContext.Decks.Add(deck);
        }
        else
        {
            dbContext.Decks.Attach(deck).State = EntityState.Modified;
        }

        PrepareQuantity(deckContext, dbContext.Holds);
        PrepareQuantity(deckContext, dbContext.Wants);
        PrepareQuantity(deckContext, dbContext.Givebacks);

        dbContext.Cards.AttachRange(cards);
    }

    private static void PrepareQuantity<TQuantity>(
        DeckContext deckContext,
        DbSet<TQuantity> dbQuantities)
        where TQuantity : Quantity
    {
        foreach (var quantity in deckContext.GetQuantities<TQuantity>())
        {
            bool isEmpty = quantity.Copies == 0;
            bool isTracked = dbQuantities.Local.Contains(quantity);

            if (!isEmpty && deckContext.IsAdded(quantity))
            {
                dbQuantities.Add(quantity);
            }
            else if (isEmpty && isTracked)
            {
                dbQuantities.Remove(quantity);
            }
            else if (!isEmpty && !isTracked)
            {
                dbQuantities.Attach(quantity);
            }
            else if (deckContext.IsModified(quantity))
            {
                dbQuantities.Attach(quantity).State = EntityState.Modified;
            }
        }
    }

    private async Task UpdateDeckFromDbAsync(
        CardDbContext dbContext,
        DbUpdateConcurrencyException ex)
    {
        if (_deckContext is null)
        {
            return;
        }

        if (HasNoDeckConflicts(_deckContext, ex))
        {
            return;
        }

        if (_cancel.IsCancellationRequested)
        {
            return;
        }

        Deck? dbDeck = null;

        try
        {
            dbDeck = await dbContext.Decks
                .Include(d => d.Holds) // unbounded: keep eye on
                    .ThenInclude(h => h.Card)
                .Include(d => d.Wants) // unbounded: keep eye on
                    .ThenInclude(w => w.Card)
                .Include(d => d.Givebacks) // unbounded: keep eye on
                    .ThenInclude(g => g.Card)

                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync(d => d.Id == _deckContext.Deck.Id, _cancel.Token);
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("Cancelled while fetching updated deck");
        }

        if (_cancel.IsCancellationRequested)
        {
            return;
        }

        if (dbDeck is null)
        {
            return;
        }

        MergeDbRemoves(_deckContext, dbDeck);
        MergeDbConflicts(dbContext, _deckContext, dbDeck);
        MergeDbAdditions(dbContext, _deckContext, dbDeck);

        CapGivebacks(_deckContext.Groups);

        dbContext.MatchToken(_deckContext.Deck, dbDeck);

        _cards.UnionWith(dbContext.Cards.Local);
    }

    private static bool HasNoDeckConflicts(
        DeckContext deckContext,
        DbUpdateConcurrencyException ex)
    {
        var deckConflict = ex.Entries<Deck>().SingleOrDefault();

        if (deckConflict is not null && deckConflict.Entity.Id == deckContext.Deck.Id)
        {
            return false;
        }

        var cardIds = deckContext.Groups
            .Select(cg => cg.CardId)
            .ToList();

        var holdConflicts = ex.Entries<Hold>()
            .IntersectBy(cardIds, e => e.Entity.CardId);

        var wantConflicts = ex.Entries<Want>()
            .IntersectBy(cardIds, e => e.Entity.CardId);

        var giveConflicts = ex.Entries<Giveback>()
            .IntersectBy(cardIds, e => e.Entity.CardId);

        return !holdConflicts.Any()
            && !wantConflicts.Any()
            && !giveConflicts.Any();
    }

    private static void MergeDbRemoves(DeckContext deckContext, Deck dbDeck)
    {
        MergeRemovedQuantity(deckContext, dbDeck.Holds);

        MergeRemovedQuantity(deckContext, dbDeck.Wants);

        MergeRemovedQuantity(deckContext, dbDeck.Givebacks);
    }

    private static void MergeRemovedQuantity<TQuantity>(
        DeckContext deckContext,
        IReadOnlyList<TQuantity> dbQuantities)
        where TQuantity : Quantity
    {
        var removedQuantities = deckContext
            .GetQuantities<TQuantity>()

            .GroupJoin(dbQuantities,
                lq => (lq.CardId, lq.LocationId),
                db => (db.CardId, db.LocationId),
                (local, dbs) =>
                    (local, noDb: !dbs.Any()))

            .Where(ln => ln.noDb)
            .Select(ln => ln.local);

        foreach (var removedQuantity in removedQuantities)
        {
            if (deckContext.IsModified(removedQuantity))
            {
                deckContext.ConvertToAddition(removedQuantity);
            }
            else
            {
                removedQuantity.Copies = 0;
            }
        }
    }

    private static void MergeDbConflicts(
        CardDbContext dbContext,
        DeckContext deckContext,
        Deck dbDeck)
    {
        MergeQuantityConflict(dbContext, deckContext, dbDeck.Holds);

        MergeQuantityConflict(dbContext, deckContext, dbDeck.Wants);

        MergeQuantityConflict(dbContext, deckContext, dbDeck.Givebacks);
    }

    private static void MergeQuantityConflict<TQuantity>(
        CardDbContext dbContext,
        DeckContext deckContext,
        IEnumerable<TQuantity> dbQuantities)
        where TQuantity : Quantity
    {
        foreach (var dbQuantity in dbQuantities)
        {
            if (!deckContext.TryGetQuantity(dbQuantity.Card, out TQuantity localQuantity))
            {
                continue;
            }

            if (!deckContext.IsModified(localQuantity))
            {
                localQuantity.Copies = dbQuantity.Copies;
            }

            dbContext.MatchToken(localQuantity, dbQuantity);
        }
    }

    private static void MergeDbAdditions(
        CardDbContext dbContext,
        DeckContext deckContext,
        Deck dbDeck)
    {
        MergeNewQuantity(dbContext, deckContext, dbDeck.Holds);

        MergeNewQuantity(dbContext, deckContext, dbDeck.Wants);

        MergeNewQuantity(dbContext, deckContext, dbDeck.Givebacks);
    }

    private static void MergeNewQuantity<TQuantity>(
        CardDbContext dbContext,
        DeckContext deckContext,
        IReadOnlyList<TQuantity> dbQuantities)
        where TQuantity : Quantity, new()
    {
        foreach (var dbQuantity in dbQuantities)
        {
            if (deckContext.TryGetQuantity(dbQuantity.Card, out TQuantity _))
            {
                continue;
            }

            var card = dbContext.Cards.Local
                .FirstOrDefault(c => c.Id == dbQuantity.CardId);

            if (card is null)
            {
                card = dbQuantity.Card;

                card.Holds.Clear();
                card.Wants.Clear();

                dbContext.Cards.Attach(card);
            }

            var newQuantity = new TQuantity
            {
                Id = dbQuantity.Id,
                Card = card,
                Location = deckContext.Deck,
                Copies = dbQuantity.Copies
            };

            // attach for nav fixup

            dbContext.Set<TQuantity>().Attach(newQuantity);

            dbContext.MatchToken(newQuantity, dbQuantity);

            deckContext.AddOriginalQuantity(newQuantity);
        }
    }

    private static void CapGivebacks(IEnumerable<QuantityGroup> deckCards)
    {
        foreach (var cardGroup in deckCards)
        {
            if (cardGroup.Giveback is null)
            {
                continue;
            }

            int currentReturn = cardGroup.Giveback.Copies;
            int copiesCap = cardGroup.Hold?.Copies ?? currentReturn;

            cardGroup.Giveback.Copies = Math.Min(currentReturn, copiesCap);
        }
    }

    #endregion
}
