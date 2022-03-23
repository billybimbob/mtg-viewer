using System;
using System.Collections.Generic;
using System.Linq;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;
using MTGViewer.Services;

namespace MTGViewer.Pages.Decks;


[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public partial class Craft : OwningComponentBase
{
    [Parameter]
    public int DeckId { get; set; } = default;

    [CascadingParameter]
    protected Task<AuthenticationState> AuthState { get; set; } = default!;


    [Inject]
    protected IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected PersistentComponentState ApplicationState { get; set; } = default!;

    [Inject]
    protected PageSizes PageSizes { get; set; } = default!;

    [Inject]
    protected NavigationManager Nav { get; set; } = default!;

    [Inject]
    protected ILogger<Craft> Logger { get; set; } = default!;


    internal bool IsLoading => _isBusy || !_isInteractive;

    internal string DeckName =>
        _deckContext?.Deck.Name is string name && !string.IsNullOrWhiteSpace(name)
            ? name : "New Deck";

    internal EditContext? DeckEdit => _deckContext?.EditContext;

    internal TreasuryFilters Filters { get; } = new();


    internal OffsetList<HeldCard> Treasury { get; private set; } = OffsetList<HeldCard>.Empty;

    internal SaveResult Result { get; set; }


    internal event EventHandler? TreasuryLoaded;

    private readonly CancellationTokenSource _cancel = new();
    private readonly SortedSet<Card> _cards = new(CardNameComparer.Instance);

    private PersistingComponentStateSubscription _persistSubscription;

    private bool _isBusy;
    private bool _isInteractive;
    private DeckContext? _deckContext;


    protected override async Task OnInitializedAsync()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistCardData);

        _isBusy = true;

        try
        {
            Filters.PageSize = PageSizes.GetComponentSize<Craft>();

            await LoadCardDataAsync(_cancel.Token);

            Filters.FilterChanged += OnFilterChange;

            TreasuryLoaded += Filters.OnTreasuryLoaded;
            TreasuryLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);
        }
        catch (Exception ex)
        {
            Logger.LogError("{Error}", ex);
        }
        finally
        {
            _isBusy = false;
        }
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await LoadDeckDataAsync(_cancel.Token);

            _isBusy = false;
            _isInteractive = true;

            StateHasChanged();
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);
        }
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _persistSubscription.Dispose();

            Filters.FilterChanged -= OnFilterChange;

            TreasuryLoaded -= Filters.OnTreasuryLoaded;

            _cancel.Cancel();
            _cancel.Dispose();
        }

        base.Dispose(disposing);
    }



    private Task PersistCardData()
    {
        ApplicationState.PersistAsJson(nameof(Treasury), Treasury as IReadOnlyList<HeldCard>);

        ApplicationState.PersistAsJson(nameof(Offset.Total), Treasury.Offset.Total);

        return Task.CompletedTask;
    }


    private async Task LoadCardDataAsync(CancellationToken cancel)
    {
        IReadOnlyList<HeldCard>? heldCards;

        if (!ApplicationState.TryTakeFromJson(nameof(Treasury), out heldCards)
            || !ApplicationState.TryTakeFromJson(nameof(Offset.Total), out int totalPages)
            || heldCards is null)
        {
            await ApplyFiltersAsync(Filters, _cancel.Token);
            return;
        }

        // only the first page will be persisted;
        var offset = new Offset(0, totalPages);

        Treasury = new OffsetList<HeldCard>(offset, heldCards);

        _cards.UnionWith(heldCards.Select(c => c.Card));
    }



    private async Task LoadDeckDataAsync(CancellationToken cancel)
    {
        var userId = await GetUserIdAsync(cancel);

        if (userId is null)
        {
            return;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(cancel);

        dbContext.Cards.AttachRange(_cards);

        var deckResult = DeckId == default
            ? await CreateDeckAsync(dbContext, userId, cancel)
            : await FetchDeckOrRedirectAsync(dbContext, userId, cancel);

        if (deckResult is null)
        {
            return;
        }

        _deckContext = new(deckResult, Filters.PageSize);

        _cards.UnionWith(dbContext.Cards.Local);
    }


    private async ValueTask<string?> GetUserIdAsync(CancellationToken cancel)
    {
        var authState = await AuthState;

        cancel.ThrowIfCancellationRequested();

        var userManager = ScopedServices.GetRequiredService<UserManager<CardUser>>();
        var userId = userManager.GetUserId(authState.User);

        if (userId is null)
        {
            Logger.LogWarning("User {User} is missing", authState.User);
        }

        return userId;
    }


    private async Task<Deck?> FetchDeckOrRedirectAsync(
        CardDbContext dbContext, string userId, CancellationToken cancel)
    {
        var deck = await CraftingDeckAsync.Invoke(dbContext, DeckId, userId, cancel);
        if (deck is null)
        {
            Nav.NavigateTo("/Decks", forceLoad: true, replace: true);
            return null;
        }

        return deck;
    }


    private static async Task<Deck> CreateDeckAsync(
        CardDbContext dbContext, string userId, CancellationToken cancel)
    {
        var user = await dbContext.Users
            .SingleOrDefaultAsync(u => u.Id == userId, cancel);

        if (user == null)
        {
            throw new ArgumentException("User cannot be found", nameof(userId));
        }

        var userDeckCount = await dbContext.Decks
            .CountAsync(d => d.OwnerId == userId, cancel);

        var newLoc = new Deck
        {
            Name = $"Deck #{userDeckCount + 1}",
            Owner = user
        };

        return newLoc;
    }



    #region Queries

    private static readonly Func<CardDbContext, int, string, CancellationToken, Task<Deck?>> CraftingDeckAsync
        = EF.CompileAsyncQuery(
            (CardDbContext dbContext, int deckId, string userId, CancellationToken _) =>

            dbContext.Decks
                .Include(d => d.Owner)
                .Include(d => d.Holds) // unbounded: keep eye on
                    .ThenInclude(h => h.Card)

                .Include(d => d.Wants) // unbounded: keep eye on
                    .ThenInclude(w => w.Card)

                .Include(d => d.GiveBacks) // unbounded: keep eye on
                    .ThenInclude(g => g.Card)

                .AsSplitQuery()
                .SingleOrDefault(d => d.Id == deckId
                    && d.OwnerId == userId
                    && !d.TradesTo.Any()));


    private static Task<OffsetList<HeldCard>> FilteredCardsAsync(
        CardDbContext dbContext,
        TreasuryFilters filters,
        CancellationToken cancel)
    {
        var cards = dbContext.Cards.AsQueryable();

        var searchName = filters.SearchName;
        var pickedColors = filters.PickedColors;

        if (!string.IsNullOrWhiteSpace(searchName))
        {
            cards = cards
                .Where(c => c.Name.ToLower()
                    .Contains(searchName.ToLower()));
        }

        if (pickedColors is not Color.None)
        {
            cards = cards
                .Where(c => (c.Color & pickedColors) == pickedColors);
        }

        int pageSize = filters.PageSize;
        int pageIndex = filters.PageIndex;

        return cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .Select(card =>
                new HeldCard(
                    card,
                    card.Holds
                        .Where(h => h.Location is Box || h.Location is Excess)
                        .Sum(h => h.Copies)))

            .PageBy(pageIndex, pageSize)
            .ToOffsetListAsync(cancel);
    }

    #endregion



    private async Task ApplyFiltersAsync(TreasuryFilters filters, CancellationToken cancel)
    {
        await using var dbContext = await DbFactory.CreateDbContextAsync(cancel);

        dbContext.Cards.AttachRange(_cards);

        Treasury = await FilteredCardsAsync(dbContext, filters, cancel);

        _cards.UnionWith(Treasury.Select(c => c.Card));
    }



    #region Treasury Operations

    public void ChangeTreasuryPage(int pageIndex) => Filters.PageIndex = pageIndex;


    internal sealed class TreasuryFilters
    {
        public event EventHandler FilterChanged;

        private bool _pendingChanges;
        private int _maxPage;

        public TreasuryFilters()
        {
            FilterChanged += (_, _) => _pendingChanges = true;
        }


        public void OnTreasuryLoaded(object? sender, EventArgs _)
        {
            if (sender is Craft parameters)
            {
                _pendingChanges = false;
                _maxPage = parameters.Treasury.Offset.Total;
            }
        }


        private int _pageSize;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (_pendingChanges
                    || value <= 0
                    || value == _pageSize)
                {
                    return;
                }

                _pageSize = value;
            }
        }

        private int _pageIndex;
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (_pendingChanges
                    || value < 0
                    || value == _pageIndex
                    || value >= _maxPage)
                {
                    return;
                }

                _pageIndex = value;

                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private string? _searchName;
        public string? SearchName
        {
            get => _searchName;
            set
            {
                if (_pendingChanges)
                {
                    return;
                }

                if (_pageIndex > 0)
                {
                    _pageIndex = 0;
                }

                _searchName = value;

                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public Color PickedColors { get; private set; }

        public void ToggleColor(Color color)
        {
            if (_pendingChanges)
            {
                return;
            }

            if (PickedColors.HasFlag(color))
            {
                PickedColors &= ~color;
            }
            else
            {
                PickedColors |= color;
            }

            if (_pageIndex > 0)
            {
                _pageIndex = 0;
            }

            FilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }



    private async void OnFilterChange(object? sender, EventArgs _)
    {
        if (_isBusy || sender is not TreasuryFilters)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await ApplyFiltersAsync(Filters, _cancel.Token);

            TreasuryLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);
        }
        catch (Exception ex)
        {
            Logger.LogError("{Error}", ex);
        }
        finally
        {
            _isBusy = false;

            StateHasChanged();
        }
    }

    #endregion



    #region Deck Operations

    public OffsetList<QuantityGroup> PagedDeckCards()
    {
        if (_deckContext is null)
        {
            return OffsetList<QuantityGroup>.Empty;
        }

        return _cards
            .Join(_deckContext.ActiveCards(),
                c => c.Id, qg => qg.CardId,
                (_, group) => group)
            .ToOffsetList(
                _deckContext.PageIndex,
                _deckContext.PageSize);
    }


    public IReadOnlyCollection<QuantityGroup> AllDeckCards()
    {
        if (_deckContext is null)
        {
            return Array.Empty<QuantityGroup>();
        }

        return _deckContext.ActiveCards().ToList();
    }


    public bool CannotSave() =>
        !_deckContext?.CanSave() ?? true;


    public Deck? GetExchangeDeck()
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
            && (deck.Wants.Any() || deck.GiveBacks.Any()))
        {
            return deck;
        }

        return null;
    }


    public void ChangeDeckPage(int pageIndex)
    {
        if (_deckContext is not null)
        {
            _deckContext.PageIndex = pageIndex;
        }
    }


    public void AddCardToDeck(Card card)
    {
        if (_deckContext is null)
        {
            return;
        }

        Result = SaveResult.None;

        if (_deckContext.TryGetQuantity(card, out GiveBack giveBack)
            && giveBack.Copies > 0)
        {
            giveBack.Copies -= 1;
            return;
        }

        if (_deckContext.Groups.Count >= PageSizes.Limit)
        {
            Logger.LogWarning("Deck want failed since at limit");
            return;
        }

        if (!_deckContext.TryGetQuantity(card, out Want want))
        {
            // use for prop fixup
            var dbContext = ScopedServices.GetRequiredService<CardDbContext>();

            want = new Want
            {
                Card = card,
                Location = _deckContext.Deck,
                Copies = 0,
            };

            dbContext.Wants.Attach(want);

            _deckContext.AddQuantity(want);

        }

        if (want.Copies >= PageSizes.Limit)
        {
            Logger.LogWarning("Deck want failed since at limit");
            return;
        }

        want.Copies += 1;
    }


    public void RemoveCardFromDeck(Card card)
    {
        if (_deckContext == null)
        {
            return;
        }

        Result = SaveResult.None;

        if (_deckContext.TryGetQuantity(card, out Want want)
            && want.Copies > 0)
        {
            want.Copies -= 1;
            return;
        }

        if (!_deckContext.TryGetQuantity(card, out Hold hold))
        {
            Logger.LogError("Card {Card} is not in the deck", card);
            return;
        }

        if (!_deckContext.TryGetQuantity(card, out GiveBack giveBack))
        {
            var dbContext = ScopedServices.GetRequiredService<CardDbContext>();

            giveBack = new GiveBack
            {
                Card = card,
                LocationId = _deckContext.Deck.Id,
                Copies = 0
            };

            dbContext.GiveBacks.Attach(giveBack);

            _deckContext.AddQuantity(giveBack);
        }

        int actualRemain = hold.Copies - giveBack.Copies;
        if (actualRemain == 0)
        {
            Logger.LogError("There are no more of {Card} to remove", card);
            return;
        }

        giveBack.Copies += 1;
    }


    private sealed class DeckContext
    {
        private readonly Dictionary<Quantity, int> _originalCopies;
        private readonly Dictionary<string, QuantityGroup> _groups;

        public bool IsNewDeck { get; private set; }

        public Deck Deck { get; }

        public EditContext EditContext { get; }

        public int PageSize { get; }

        private int _pageIndex;
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (value < 0
                    || value == _pageIndex
                    || value >= GetOffset().Total)
                {
                    return;
                }

                _pageIndex = value;
            }
        }

        public IReadOnlyCollection<QuantityGroup> Groups => _groups.Values;


        public DeckContext(Deck deck, int pageSize)
        {
            ArgumentNullException.ThrowIfNull(deck);

            _groups = QuantityGroup
                .FromDeck(deck)
                .ToDictionary(cg => cg.CardId);

            _originalCopies = new();

            IsNewDeck = deck.Id == default;

            Deck = deck;

            EditContext = new(deck);

            PageSize = pageSize;

            UpdateOriginals();
        }


        public IEnumerable<QuantityGroup> ActiveCards() =>
            _groups.Values.Where(qg => qg.Total > 0);


        public Offset GetOffset()
        {
            int totalActive = ActiveCards().Count();

            return new Offset(_pageIndex, totalActive, PageSize);
        }

        public bool IsAdded(Quantity quantity)
        {
            return !_originalCopies.ContainsKey(quantity);
        }

        public bool IsModified(Quantity quantity)
        {
            return quantity.Copies != _originalCopies.GetValueOrDefault(quantity);
        }


        public bool CanSave()
        {
            if (!EditContext.Validate())
            {
                return false;
            }

            if (IsNewDeck)
            {
                return true;
            }

            if (EditContext.IsModified())
            {
                return true;
            }

            bool quantitiesModifed = _groups.Values
                .SelectMany(cg => cg)
                .Any(q => IsModified(q));

            if (quantitiesModifed)
            {
                return true;
            }

            return false;
        }


        public IEnumerable<TQuantity> GetQuantities<TQuantity>()
            where TQuantity : Quantity
        {
            var quantityType = typeof(TQuantity);

            if (quantityType == typeof(Hold))
            {
                return Deck.Holds.OfType<TQuantity>();
            }
            else if (quantityType == typeof(Want))
            {
                return Deck.Wants.OfType<TQuantity>();
            }
            // else if (quantityType == typeof(GiveBack))
            else
            {
                return Deck.GiveBacks.OfType<TQuantity>();
            }
        }


        public bool TryGetQuantity<TQuantity>(Card card, out TQuantity quantity)
            where TQuantity : Quantity
        {
            quantity = null!;

            if (card is null)
            {
                return false;
            }

            if (!_groups.TryGetValue(card.Id, out var group))
            {
                return false;
            }

            quantity = group.GetQuantity<TQuantity>()!;

            return quantity != null;
        }


        public void AddQuantity<TQuantity>(TQuantity quantity)
            where TQuantity : Quantity
        {
            ArgumentNullException.ThrowIfNull(quantity);

            if (!_groups.TryGetValue(quantity.CardId, out var group))
            {
                _groups.Add(quantity.CardId, new QuantityGroup(quantity));
                return;
            }

            if (group.GetQuantity<TQuantity>() is not null)
            {
                return;
            }

            switch (quantity)
            {
                case Hold hold:
                    Deck.Holds.Add(hold);
                    break;

                case Want want:
                    Deck.Wants.Add(want);
                    break;

                case GiveBack giveBack:
                    Deck.GiveBacks.Add(giveBack);
                    break;

                default:
                    throw new ArgumentException($"Unexpected Quantity type {quantity.GetType().Name}", nameof(quantity));
            }

            group.AddQuantity(quantity);
        }


        public void AddOriginalQuantity(Quantity quantity)
        {
            ArgumentNullException.ThrowIfNull(quantity);

            AddQuantity(quantity);

            _originalCopies.Add(quantity, quantity.Copies);
        }


        public void ConvertToAddition(Quantity quantity)
        {
            ArgumentNullException.ThrowIfNull(quantity);

            _originalCopies.Remove(quantity);
        }


        private void UpdateOriginals()
        {
            var allQuantities = _groups.Values.SelectMany(qg => qg);

            foreach (var quantity in allQuantities)
            {
                _originalCopies[quantity] = quantity.Copies;
            }
        }


        public void SuccessfullySaved()
        {
            UpdateOriginals();

            IsNewDeck = false;
        }
    }

    #endregion



    #region Save Changes

    public async Task CommitChangesAsync()
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
            var token = _cancel.Token;
            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            dbContext.Cards.AttachRange(_cards);

            Result = await SaveOrConcurrentRecoverAsync(dbContext, _deckContext, token);
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


    private async Task<SaveResult> SaveOrConcurrentRecoverAsync(
        CardDbContext dbContext,
        DeckContext deckContext,
        CancellationToken cancel)
    {
        try
        {
            PrepareChanges(dbContext, deckContext);

            await dbContext.SaveChangesAsync(cancel);

            deckContext.SuccessfullySaved();

            return SaveResult.Success;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await UpdateDeckFromDbAsync(dbContext, deckContext, ex, cancel);

            _cards.UnionWith(dbContext.Cards.Local);

            return SaveResult.Error;
        }
    }


    private static void PrepareChanges(CardDbContext dbContext, DeckContext deckContext)
    {
        var deck = deckContext.Deck;

        dbContext.Users.Attach(deck.Owner);

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
        PrepareQuantity(deckContext, dbContext.GiveBacks);
    }


    private static void PrepareQuantity<TQuantity>(
        DeckContext deckContext,
        DbSet<TQuantity> dbQuantities)
        where TQuantity : Quantity
    {
        foreach (var quantity in deckContext.GetQuantities<TQuantity>())
        {
            bool isEmpty = quantity.Copies == 0;

            if (deckContext.IsAdded(quantity) && !isEmpty)
            {
                dbQuantities.Add(quantity);
            }
            else if (deckContext.IsModified(quantity))
            {
                if (isEmpty)
                {
                    dbQuantities.Remove(quantity);
                }
                else
                {
                    dbQuantities.Attach(quantity).State = EntityState.Modified;
                }
            }
            else if (!isEmpty)
            {
                dbQuantities.Attach(quantity);
            }
        }
    }


    private static async Task UpdateDeckFromDbAsync(
        CardDbContext dbContext,
        DeckContext deckContext,
        DbUpdateConcurrencyException ex,
        CancellationToken cancel)
    {
        if (HasNoDeckConflicts(deckContext, ex))
        {
            return;
        }

        if (cancel.IsCancellationRequested)
        {
            return;
        }

        Deck? dbDeck = null;
        var localDeck = deckContext.Deck;

        try
        {
            dbDeck = await dbContext.Decks
                .Include(d => d.Holds) // unbounded: keep eye on
                    .ThenInclude(h => h.Card)
                .Include(d => d.Wants) // unbounded: keep eye on
                    .ThenInclude(w => w.Card)
                .Include(d => d.GiveBacks) // unbounded: keep eye on
                    .ThenInclude(g => g.Card)

                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync(d => d.Id == localDeck.Id, cancel);
        }
        catch (OperationCanceledException)
        { }

        if (cancel.IsCancellationRequested)
        {
            return;
        }

        if (dbDeck == default)
        {
            return;
        }

        MergeDbRemoves(deckContext, dbDeck);

        MergeDbConflicts(dbContext, deckContext, dbDeck);

        MergeDbAdditions(dbContext, deckContext, dbDeck);

        CapGiveBacks(deckContext.Groups);

        dbContext.MatchToken(localDeck, dbDeck);
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

        var giveConflicts = ex.Entries<GiveBack>()
            .IntersectBy(cardIds, e => e.Entity.CardId);

        return !holdConflicts.Any()
            && !wantConflicts.Any()
            && !giveConflicts.Any();
    }


    private static void MergeDbRemoves(DeckContext deckContext, Deck dbDeck)
    {
        MergeRemovedQuantity(deckContext, dbDeck.Holds);

        MergeRemovedQuantity(deckContext, dbDeck.Wants);

        MergeRemovedQuantity(deckContext, dbDeck.GiveBacks);
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

        MergeQuantityConflict(dbContext, deckContext, dbDeck.GiveBacks);
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
        MergeNewQuantity(deckContext, dbContext, dbDeck.Holds);

        MergeNewQuantity(deckContext, dbContext, dbDeck.Wants);

        MergeNewQuantity(deckContext, dbContext, dbDeck.GiveBacks);
    }


    private static void MergeNewQuantity<TQuantity>(
        DeckContext deckContext,
        CardDbContext dbContext,
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

            if (card == default)
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
            AttachQuantity(dbContext, newQuantity);
            dbContext.MatchToken(newQuantity, dbQuantity);

            deckContext.AddOriginalQuantity(newQuantity);
        }
    }


    private static void AttachQuantity<TQuantity>(CardDbContext dbContext, TQuantity quantity)
        where TQuantity : Quantity
    {
        switch (quantity)
        {
            case Hold hold:
                dbContext.Holds.Attach(hold);
                break;

            case Want want:
                dbContext.Wants.Attach(want);
                break;

            case GiveBack giveBack:
                dbContext.GiveBacks.Attach(giveBack);
                break;

            default:
                throw new ArgumentException($"Unexpected Quantity type {typeof(TQuantity).Name}", nameof(quantity));
        }
    }


    private static void CapGiveBacks(IEnumerable<QuantityGroup> deckCards)
    {
        foreach (var cardGroup in deckCards)
        {
            if (cardGroup.GiveBack is null)
            {
                continue;
            }

            var currentReturn = cardGroup.GiveBack.Copies;
            var copiesCap = cardGroup.Hold?.Copies ?? currentReturn;

            cardGroup.GiveBack.Copies = Math.Min(currentReturn, copiesCap);
        }
    }

    #endregion
}
