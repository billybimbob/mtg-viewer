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
    public Task<AuthenticationState> AuthState { get; set; } = default!;


    [Inject]
    protected IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected PageSizes PageSizes { get; set; } = default!;

    [Inject]
    protected NavigationManager NavManager { get; set; } = default!;

    [Inject]
    protected ILogger<Craft> Logger { get; set; } = default!;


    public bool IsBusy => _isBusy;

    public OffsetList<CardTotal> Treasury => _pagedCards ?? OffsetList<CardTotal>.Empty;

    public string DeckName =>
        _deckContext?.Deck.Name is string name && !string.IsNullOrWhiteSpace(name) 
            ? name : "New Deck";

    public EditContext? DeckEdit => _deckContext?.EditContext;

    public SaveResult Result { get; set; }


    private const int SearchNameLimit = 40;

    private bool _isBusy;
    private readonly CancellationTokenSource _cancel = new();

    private readonly SortedSet<Card> _cards = new(new CardNameComparer());

    private DeckContext? _deckContext;

    private readonly TreasuryFilters _treasuryFilters = new();
    private OffsetList<CardTotal>? _pagedCards;


    protected override async Task OnInitializedAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            _treasuryFilters.PageSize = PageSizes.GetComponentSize<Craft>();

            await ApplyFiltersAsync(_treasuryFilters, _cancel.Token);

            _treasuryFilters.SetLoader(new TreasuryLoader(this));
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogError(ex.ToString());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex.ToString());
        }
        finally
        {
            _isBusy = false;
        }
    }


    protected override async Task OnParametersSetAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            var userId = await GetUserIdAsync();
            if (userId is null)
            {
                return;
            }

            var cancelToken = _cancel.Token;
            await using var dbContext = await DbFactory.CreateDbContextAsync(cancelToken);

            dbContext.Cards.AttachRange(_cards);

            var deckResult = DeckId == default
                ? await CreateDeckAsync(dbContext, userId, cancelToken)
                : await FetchDeckOrRedirectAsync(dbContext, userId, cancelToken);

            if (deckResult is null)
            {
                return;
            }

            _deckContext = new(deckResult, _treasuryFilters.PageSize);

            _cards.UnionWith(dbContext.Cards.Local);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogError(ex.ToString());
        }
        catch (NavigationException ex)
        {
            Logger.LogWarning(ex.ToString());
        }

        finally
        {
            _isBusy = false;
        }
    }


    private async Task<string?> GetUserIdAsync()
    {
        if (AuthState is null)
        {
            return null;
        }

        var userManager = ScopedServices.GetRequiredService<UserManager<CardUser>>();
        var authState = await AuthState;

        return userManager.GetUserId(authState.User);
    }


    private async Task<Deck?> FetchDeckOrRedirectAsync(
        CardDbContext dbContext, string userId, CancellationToken cancel)
    {
        var deck = await CraftingDeckAsync(dbContext, DeckId, userId, cancel);

        if (deck is null || deck.OwnerId != userId)
        {
            NavManager.NavigateTo("/Decks", true);

            return null;
        }

        if (deck.TradesTo.Any())
        {
            NavManager.NavigateTo($"/Decks/Details/{DeckId}", true);

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
            throw new ArgumentException(nameof(userId));
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


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancel.Cancel();
            _cancel.Dispose();
        }

        base.Dispose(disposing);
    }



    private async Task ApplyFiltersAsync(TreasuryFilters filters, CancellationToken cancel)
    {
        await using var dbContext = await DbFactory.CreateDbContextAsync(cancel);

        dbContext.Cards.AttachRange(_cards);

        _pagedCards = await FilteredCardsAsync(dbContext, filters, cancel);

        _cards.UnionWith(_pagedCards.Select(c => c.Card));
    }


    #region Queries

    private static Task<Deck?> CraftingDeckAsync(
        CardDbContext dbContext,
        int deckId,
        string userId,
        CancellationToken cancel)
    {
        return dbContext.Decks
            .Include(d => d.Cards) // unbounded: keep eye on
                .ThenInclude(a => a.Card)

            .Include(d => d.Wants) // unbounded: keep eye on
                .ThenInclude(a => a.Card)

            .Include(d => d.GiveBacks) // unbounded: keep eye on
                .ThenInclude(a => a.Card)

            .Include(d => d.TradesTo
                .OrderBy(t => t.Id)
                .Take(1))

            .AsSplitQuery()
            .SingleOrDefaultAsync(d =>
                d.Id == deckId && d.OwnerId == userId, cancel);
    }


    private static Task<OffsetList<CardTotal>> FilteredCardsAsync(
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

            .PageBy(pageIndex, pageSize)
            .Select(c => new CardTotal(c, c.Amounts.Sum(a => a.NumCopies)))
            .ToOffsetListAsync(cancel);
    }


    private static Task<Dictionary<string, int>> BoxAmountsAsync(
        CardDbContext dbContext,
        IEnumerable<Card> cards,
        CancellationToken cancel)
    {
        var cardIds = cards
            .Select(c => c.Id)
            .ToArray();

        return dbContext.Amounts
            .Where(a => a.Location is Box && cardIds.Contains(a.CardId))
            .GroupBy(a => a.CardId,
                (CardId, amounts) => 
                    new { CardId, Total = amounts.Sum(a => a.NumCopies) })

            .ToDictionaryAsync(
                ct => ct.CardId, ct => ct.Total, cancel);
    }

    #endregion



    #region Treasury Operations

    public string ActiveColor(Color color) =>
        _treasuryFilters.PickedColors.HasFlag(color) ? "active" : string.Empty;

    public void ToggleColor(Color color) => _treasuryFilters.ToggleColor(color);

    public void ChangeTreasuryPage(int pageIndex) => _treasuryFilters.PageIndex = pageIndex;


    private sealed class TreasuryFilters
    {
        private TreasuryLoader _loader;
        internal void SetLoader(TreasuryLoader treasuryLoader)
        {
            _loader = treasuryLoader;
        }

        private int _pageSize;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (_loader.IsBusy
                    || value <= 0
                    || value == _pageSize)
                {
                    return;
                }

                if (_pageIndex > 0)
                {
                    _pageIndex = 0;
                }

                _pageSize = value;
                _loader.LoadCardsAsync();
            }
        }

        private int _pageIndex;
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (_loader.IsBusy
                    || value < 0
                    || value == _pageIndex
                    || value >= _loader.MaxPage)
                {
                    return;
                }

                _pageIndex = value;

                _loader.LoadCardsAsync();
            }
        }

        private string? _searchName;
        public string? SearchName
        {
            get => _searchName;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = null;
                }

                if (_loader.IsBusy
                    || value?.Length > SearchNameLimit
                    || value == _searchName)
                {
                    return;
                }

                if (_pageIndex > 0)
                {
                    _pageIndex = 0;
                }

                _searchName = value;

                _loader.LoadCardsAsync();
            }
        }

        private Color _pickedColors;
        public Color PickedColors => _pickedColors;

        public void ToggleColor(Color color)
        {
            if (_loader.IsBusy)
            {
                return;
            }

            if (_pickedColors.HasFlag(color))
            {
                _pickedColors &= ~color;
            }
            else
            {
                _pickedColors |= color;
            }

            if (_pageIndex > 0)
            {
                _pageIndex = 0;
            }

            _loader.LoadCardsAsync();
        }
    }


    internal readonly struct TreasuryLoader
    {
        private readonly Craft? _parent;

        public TreasuryLoader(Craft parent)
        {
            _parent = parent;
        }

        public int MaxPage => _parent?.Treasury.Offset.Total ?? 0;

        public bool IsBusy => _parent?.IsBusy ?? false;

        public Task LoadCardsAsync()
        {
            return _parent is null
                ? Task.CompletedTask
                : _parent.LoadCardsAsync();
        }
    }


    private async Task LoadCardsAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await ApplyFiltersAsync(_treasuryFilters, _cancel.Token);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogError(ex.ToString());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex.ToString());
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
            .Join( _deckContext.ActiveCards(),
                c => c.Id,
                qg => qg.CardId,
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
            && giveBack.NumCopies > 0)
        {
            giveBack.NumCopies -= 1;
            return;
        }

        if (_deckContext.Groups.Count >= PageSizes.Limit)
        {
            Logger.LogWarning("deck want failed since at limit");
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
                NumCopies = 0,
            };

            dbContext.Wants.Attach(want);

            _deckContext.AddQuantity(want);

        }

        if (want.NumCopies >= PageSizes.Limit)
        {
            Logger.LogWarning("deck want failed since at limit");
            return;
        }

        want.NumCopies += 1;
    }


    public void RemoveCardFromDeck(Card card)
    {
        if (_deckContext == null)
        {
            return;
        }

        Result = SaveResult.None;

        if (_deckContext.TryGetQuantity(card, out Want want)
            && want.NumCopies > 0)
        {
            want.NumCopies -= 1;
            return;
        }

        if (!_deckContext.TryGetQuantity(card, out Amount amount))
        {
            Logger.LogError($"card {card.Id} is not in the deck");
            return;
        }

        if (!_deckContext.TryGetQuantity(card, out GiveBack giveBack))
        {
            var dbContext = ScopedServices.GetRequiredService<CardDbContext>();

            giveBack = new GiveBack
            {
                Card = card,
                LocationId = _deckContext.Deck.Id,
                NumCopies = 0
            };

            dbContext.GiveBacks.Attach(giveBack);

            _deckContext.AddQuantity(giveBack);
        }

        int actualRemain = amount.NumCopies - giveBack.NumCopies;
        if (actualRemain == 0)
        {
            Logger.LogError($"there are no more of {card.Id} to remove");
            return;
        }

        giveBack.NumCopies += 1;
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
                    || value >= Offset().Total)
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


        public Offset Offset()
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
            return _originalCopies.TryGetValue(quantity, out int numCopies)
                ? quantity.NumCopies != numCopies
                : quantity.NumCopies != 0;
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

            if (quantityType == typeof(Amount))
            {
                return Deck.Cards.OfType<TQuantity>();
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

            quantity = GetQuantity<TQuantity>(group)!;

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

            if (GetQuantity<TQuantity>(group) is not null)
            {
                return;
            }

            switch (quantity)
            {
                case Amount amount:
                    group.Amount = amount;
                    Deck.Cards.Add(amount);
                    break;

                case Want want:
                    group.Want = want;
                    Deck.Wants.Add(want);
                    break;

                case GiveBack giveBack:
                    group.GiveBack = giveBack;
                    Deck.GiveBacks.Add(giveBack);
                    break;

                default:
                    throw new ArgumentException(nameof(quantity));
            }
        }


        private TQuantity? GetQuantity<TQuantity>(QuantityGroup group)
            where TQuantity : Quantity
        {
            return group.Amount as TQuantity
                ?? group.Want as TQuantity
                ?? group.GiveBack as TQuantity;
        }


        public void AddOriginalQuantity(Quantity quantity)
        {
            ArgumentNullException.ThrowIfNull(quantity);

            AddQuantity(quantity);

            _originalCopies.Add(quantity, quantity.NumCopies);
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
                _originalCopies[quantity] = quantity.NumCopies;
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
            var cancelToken = _cancel.Token;
            await using var dbContext = await DbFactory.CreateDbContextAsync(cancelToken);

            dbContext.Cards.AttachRange(_cards);

            Result = await SaveOrConcurrentRecoverAsync(dbContext, _deckContext, cancelToken);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogError(ex.ToString());
        }
        catch (DbUpdateException ex)
        {
            Logger.LogError(ex.ToString());

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

        if (deckContext.IsNewDeck)
        {
            dbContext.Decks.Add(deck);
        }
        else
        {
            dbContext.Decks.Attach(deck).State = EntityState.Modified;
        }

        PrepareQuantity(deckContext, dbContext.Amounts);
        PrepareQuantity(deckContext, dbContext.Wants);
        PrepareQuantity(deckContext, dbContext.GiveBacks);

        deck.UpdateColors();
    }


    private static void PrepareQuantity<TQuantity>(
        DeckContext deckContext,
        DbSet<TQuantity> dbQuantities)
        where TQuantity : Quantity
    {
        foreach (var quantity in deckContext.GetQuantities<TQuantity>())
        {
            bool isEmpty = quantity.NumCopies == 0;

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
                .Include(d => d.Cards) // unbounded: keep eye on
                    .ThenInclude(a => a.Card)
                .Include(d => d.Wants) // unbounded: keep eye on
                    .ThenInclude(a => a.Card)
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

        var amountConflicts = ex.Entries<Amount>()
            .IntersectBy(cardIds, e => e.Entity.CardId);

        var wantConflicts = ex.Entries<Want>()
            .IntersectBy(cardIds, e => e.Entity.CardId);

        var giveConflicts = ex.Entries<GiveBack>()
            .IntersectBy(cardIds, e => e.Entity.CardId);

        return !amountConflicts.Any()
            && !wantConflicts.Any()
            && !giveConflicts.Any();
    }


    private static void MergeDbRemoves(DeckContext deckContext, Deck dbDeck)
    {
        MergeRemovedQuantity(deckContext, dbDeck.Cards);

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
                removedQuantity.NumCopies = 0;
            }
        }
    }



    private static void MergeDbConflicts(
        CardDbContext dbContext,
        DeckContext deckContext,
        Deck dbDeck)
    {
        MergeQuantityConflict(dbContext, deckContext, dbDeck.Cards);

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
                localQuantity.NumCopies = dbQuantity.NumCopies;
            }

            dbContext.MatchToken(localQuantity, dbQuantity);
        }
    }


    private static void MergeDbAdditions(
        CardDbContext dbContext, 
        DeckContext deckContext, 
        Deck dbDeck)
    {
        MergeNewQuantity(deckContext, dbContext, dbDeck.Cards);

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

                card.Amounts.Clear();
                card.Wants.Clear();

                dbContext.Cards.Attach(card);
            }

            var newQuantity = new TQuantity
            {
                Id = dbQuantity.Id,
                Card = card,
                Location = deckContext.Deck,
                NumCopies = dbQuantity.NumCopies
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
            case Amount amount:
                dbContext.Amounts.Attach(amount);
                break;

            case Want want:
                dbContext.Wants.Attach(want);
                break;

            case GiveBack giveBack:
                dbContext.GiveBacks.Attach(giveBack);
                break;

            default:
                throw new ArgumentException(typeof(TQuantity).Name);
        }
    }


    private static void CapGiveBacks(IEnumerable<QuantityGroup> deckCards)
    {
        foreach(var cardGroup in deckCards)
        {
            if (cardGroup.GiveBack is null)
            {
                continue;
            }

            var currentReturn = cardGroup.GiveBack.NumCopies;
            var capAmount = cardGroup.Amount?.NumCopies ?? currentReturn;

            cardGroup.GiveBack.NumCopies = Math.Min(currentReturn, capAmount);
        }
    }

    #endregion
}