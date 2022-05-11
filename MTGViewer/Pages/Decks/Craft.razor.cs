using System;
using System.Collections.Generic;
using System.Linq;
using System.Paging;
using System.Text.Json.Serialization;
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
using MTGViewer.Services;
using MTGViewer.Utils;

namespace MTGViewer.Pages.Decks;

[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public partial class Craft : OwningComponentBase
{
    [Parameter]
    public int DeckId { get; set; }

    [CascadingParameter]
    protected Task<AuthenticationState> AuthState { get; set; } = default!;

    [Inject]
    protected IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected PersistentComponentState ApplicationState { get; set; } = default!;

    [Inject]
    protected ParseTextFilter ParseTextFilter { get; set; } = default!;

    [Inject]
    protected PageSize PageSize { get; set; } = default!;

    [Inject]
    protected NavigationManager Nav { get; set; } = default!;

    [Inject]
    protected ILogger<Craft> Logger { get; set; } = default!;

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal BuildType BuildOption { get; private set; }

    internal SaveResult Result { get; set; }

    private readonly CancellationTokenSource _cancel = new();
    private readonly SortedSet<Card> _cards = new(CardNameComparer.Instance);

    private PersistingComponentStateSubscription _persistSubscription;

    private bool _isBusy;
    private bool _isInteractive;

    protected override void OnInitialized()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistCardDataAsync);
    }

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

        await InitializeHoldsAsync(dbContext);
        await InitializeReturnsAsync(dbContext);
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

        dbContext.Cards.AttachRange(cards);
        dbContext.Decks.Attach(_deckContext.Deck);

        _cards.UnionWith(cards);

        InitializeHolds();
        InitializeReturns();

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

    private async Task CreateDeckOrRedirectAsync(CardDbContext dbContext, string? userId)
    {
        if (userId is null)
        {
            Nav.NavigateTo("/Decks", forceLoad: true, replace: true);
            return;
        }

        bool userExists = await dbContext.Users
            .AnyAsync(u => u.Id == userId, _cancel.Token);

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

        var preview = await DeckCountsAsync.Invoke(dbContext, DeckId, _cancel.Token);

        if (preview is null || preview.OwnerId != userId || preview.HasTrades)
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

        _counts = preview;

        _deckContext = new DeckContext(deck);
    }

    private static readonly Func<CardDbContext, int, CancellationToken, Task<DeckCounts?>> DeckCountsAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, CancellationToken _) =>
            dbContext.Decks
                .Where(d => d.Id == deckId)
                .Select(d => new DeckCounts
                {
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

    internal async Task UpdateBuildTypeAsync(ChangeEventArgs args)
    {
        if (_isBusy || !Enum.TryParse(args.Value?.ToString(), out BuildType value))
        {
            return;
        }

        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            if (value is BuildType.Holds)
            {
                await InitializeHoldsAsync(dbContext);
                await InitializeReturnsAsync(dbContext);
            }
            else
            {
                await InitializeWantsAsync(dbContext);
                await ApplyFiltersAsync(dbContext);
            }

            BuildOption = value;
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

    public enum BuildType
    {
        Holds,
        Theorycrafting
    }

    private sealed class DeckDto : ConcurrentDto
    {
        public int Id { get; init; }
        public string OwnerId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public Color Color { get; init; }

        public IEnumerable<QuantityDto> Holds { get; init; } = Enumerable.Empty<QuantityDto>();
        public IEnumerable<QuantityDto> Wants { get; init; } = Enumerable.Empty<QuantityDto>();
        public IEnumerable<QuantityDto> Givebacks { get; init; } = Enumerable.Empty<QuantityDto>();

        [JsonConstructor]
        public DeckDto()
        { }

        public DeckDto(CardDbContext dbContext, DeckContext deckContext)
        {
            var deck = deckContext.Deck;

            Id = deck.Id;
            OwnerId = deck.OwnerId;

            Name = deck.Name;
            Color = deck.Color;

            Wants = deck.Wants.Select(w => new QuantityDto(dbContext, w));
            Holds = deck.Holds.Select(h => new QuantityDto(dbContext, h));
            Givebacks = deck.Givebacks.Select(g => new QuantityDto(dbContext, g));

            dbContext.CopyToken(this, deck);
        }

        public DeckContext ToDeckContext(CardDbContext dbContext)
        {
            var deck = new Deck();

            dbContext.Entry(deck).CurrentValues.SetValues(this);

            deck.Holds.AddRange(
                Holds.Select(q => q.ToQuantity<Hold>(dbContext)));

            deck.Wants.AddRange(
                Wants.Select(q => q.ToQuantity<Want>(dbContext)));

            deck.Givebacks.AddRange(
                Givebacks.Select(q => q.ToQuantity<Giveback>(dbContext)));

            return new DeckContext(deck);
        }
    }

    private sealed class QuantityDto : ConcurrentDto
    {
        public int Id { get; init; }

        public string CardId { get; init; } = string.Empty;

        public int Copies { get; init; }

        [JsonConstructor]
        public QuantityDto()
        { }

        public QuantityDto(CardDbContext dbContext, Quantity quantity)
        {
            Id = quantity.Id;
            CardId = quantity.CardId;
            Copies = quantity.Copies;

            dbContext.CopyToken(this, quantity);
        }

        public TQuantity ToQuantity<TQuantity>(CardDbContext dbContext) where TQuantity : Quantity, new()
        {
            var quantity = new TQuantity();

            dbContext.Entry(quantity).CurrentValues.SetValues(this);

            return quantity;
        }
    }

    private sealed record DeckCounts
    {
        public string OwnerId { get; init; } = string.Empty;

        public int HeldCopies { get; init; }
        public int WantCopies { get; set; }
        public int ReturnCopies { get; set; }

        public int HeldCount { get; init; }
        public int WantCount { get; set; }
        public int ReturnCount { get; set; }

        public bool HasTrades { get; init; }
    }

    private readonly record struct LoadedSeekList<T> where T : class
    {
        public SeekRequest<T> Request { get; }
        public SeekList<T>? List { get; }

        public LoadedSeekList(SeekRequest<T> request, SeekList<T> list)
        {
            Request = request;
            List = list;
        }
    }

    private sealed class DeckContext
    {
        private readonly Dictionary<Quantity, int> _originalCopies;
        private readonly Dictionary<string, QuantityGroup> _groups;

        public DeckContext(Deck deck)
        {
            ArgumentNullException.ThrowIfNull(deck);

            _originalCopies = new Dictionary<Quantity, int>();

            _groups = QuantityGroup
                .FromDeck(deck)
                .ToDictionary(qg => qg.CardId);

            Deck = deck;
            EditContext = new EditContext(deck);

            IsNewDeck = deck.Id == default;

            UpdateOriginals();
        }

        public Deck Deck { get; }

        public EditContext EditContext { get; }

        public bool IsNewDeck { get; private set; }

        public IReadOnlyCollection<QuantityGroup> Groups => _groups.Values;

        public bool IsAdded(Quantity quantity)
            => !_originalCopies.ContainsKey(quantity);

        public bool IsModified(Quantity quantity)
            => quantity.Copies != _originalCopies.GetValueOrDefault(quantity);

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
            // else if (quantityType == typeof(Giveback))
            else
            {
                return Deck.Givebacks.OfType<TQuantity>();
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

            group.AddQuantity(quantity);
        }

        public void AddOriginalQuantity<TQuantity>(TQuantity quantity)
            where TQuantity : Quantity
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
}
