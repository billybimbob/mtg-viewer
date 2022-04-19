using System;
using System.Collections.Generic;
using System.Linq;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Utils;

namespace MTGViewer.Pages.Decks;

[Authorize]
public partial class Mulligan : OwningComponentBase
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
    protected NavigationManager Nav { get; set; } = default!;

    [Inject]
    protected PageSize PageSize { get; set; } = default!;

    [Inject]
    protected IOptions<MulliganOptions> Options { get; set; } = default!;

    [Inject]
    protected ILogger<Mulligan> Logger { get; set; } = default!;

    private readonly CancellationTokenSource _cancel = new();
    private readonly HashSet<string> _loadedImages = new();

    private PersistingComponentStateSubscription _persistSubscription;

    private bool _isBusy;
    private bool _isInteractive;

    private MulliganTarget? _target;
    private MulliganType _mulliganType;
    private DrawSimulation? _shuffledDeck;

    protected override void OnInitialized()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistDeckData);
    }

    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            string? userId = await GetUserIdAsync(_cancel.Token);

            if (userId is null)
            {
                Nav.NavigateTo("/Cards", forceLoad: true, replace: true);
                return;
            }

            _target = await GetMulliganDataAsync(userId, _cancel.Token);

            if (_target is null)
            {
                Nav.NavigateTo("/Decks", forceLoad: true, replace: true);
                return;
            }

            if (!_target.Cards.Any())
            {
                Nav.NavigateTo($"/Decks/Details/{DeckId}", forceLoad: true);
            }

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
            _cancel.Cancel();
            _cancel.Dispose();

            _persistSubscription.Dispose();

            _shuffledDeck?.Dispose();
        }

        base.Dispose(disposing);
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
            return null;
        }

        return userId;
    }

    private Task PersistDeckData()
    {
        ApplicationState.PersistAsJson(nameof(_target), _target);

        return Task.CompletedTask;
    }

    private async Task<MulliganTarget?> GetMulliganDataAsync(string userId, CancellationToken cancel)
    {
        if (ApplicationState.TryGetData(nameof(_target), out MulliganTarget? target))
        {
            return target;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(cancel);

        string? name = await dbContext.Decks
            .Where(d => d.Id == DeckId && d.OwnerId == userId)
            .Select(d => d.Name)
            .SingleOrDefaultAsync(cancel);

        if (name is null)
        {
            return null;
        }

        return new MulliganTarget
        {
            Name = name,
            Cards = await DeckCardsAsync
                .Invoke(dbContext, DeckId, PageSize.Limit)
                .ToListAsync(cancel)
        };
    }

    private static readonly Func<CardDbContext, int, int, IAsyncEnumerable<DeckCopy>> DeckCardsAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, int limit) =>
            dbContext.Cards
                .Where(c => c.Holds.Any(h => h.LocationId == deckId)
                    || c.Wants.Any(w => w.LocationId == deckId)
                    || c.Givebacks.Any(g => g.LocationId == deckId))

                .OrderBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id)

                .Take(limit)
                .Select(c => new DeckCopy
                {
                    Id = c.Id,
                    Name = c.Name,
                    ManaCost = c.ManaCost,

                    SetName = c.SetName,
                    Rarity = c.Rarity,
                    ImageUrl = c.ImageUrl,

                    Held = c.Holds
                        .Where(h => h.LocationId == deckId)
                        .Sum(h => h.Copies),

                    Want = c.Wants
                        .Where(w => w.LocationId == deckId)
                        .Sum(w => w.Copies),

                    Returning = c.Givebacks
                        .Where(g => g.LocationId == deckId)
                        .Sum(g => g.Copies)
                }));

    #region View Properties

    internal string DeckName => _target?.Name ?? "Deck Mulligan";

    internal IReadOnlyList<CardPreview> Hand => _shuffledDeck?.Hand ?? Array.Empty<CardPreview>();

    internal bool CanDraw => _shuffledDeck?.CanDraw ?? false;

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal MulliganType DeckMulligan
    {
        get => _mulliganType;
        set
        {
            if (!_isBusy && _isInteractive && _mulliganType != value)
            {
                _mulliganType = value;

                _ = NewHandAsync();
            }
        }
    }

    #endregion

    internal async Task NewHandAsync()
    {
        if (_isBusy || !_isInteractive || _target is null)
        {
            return;
        }

        _isBusy = true;

        try
        {
            _shuffledDeck?.Dispose();

            if (_mulliganType is MulliganType.None)
            {
                _shuffledDeck = null;
                return;
            }

            _shuffledDeck = new DrawSimulation(_target.Cards, _mulliganType);

            int requiredCards = Options.Value.HandSize;

            while (requiredCards > 0 && _shuffledDeck.CanDraw)
            {
                await Task.Delay(Options.Value.DrawInterval, _cancel.Token);

                _shuffledDeck.DrawCard();

                requiredCards -= 1;

                StateHasChanged();
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    internal void DrawCard()
    {
        if (!_isBusy
            && _isInteractive
            && _shuffledDeck is { CanDraw: true })
        {
            _shuffledDeck.DrawCard();
        }
    }

    internal bool IsImageLoaded(CardPreview card)
    {
        return _loadedImages.Contains(card.Id);
    }

    internal void OnImageLoad(CardPreview card)
    {
        _loadedImages.Add(card.Id);
    }

    private sealed class DrawSimulation : IDisposable
    {
        private static readonly ObjectPool<CardCopy> _cardPool
            = new DefaultObjectPool<CardCopy>(
                new DefaultPooledObjectPolicy<CardCopy>());

        private readonly ICollection<CardCopy> _cardOptions;
        private readonly List<CardPreview> _hand;

        private int _cardsInDeck;
        private CardCopy? _nextDraw;

        public DrawSimulation(IReadOnlyList<DeckCopy> deck, MulliganType mulliganType)
        {
            _cardOptions = deck
                .Select(d =>
                {
                    var copy = _cardPool.Get();
                    copy.Card = d;
                    copy.Copies = GetCopies(d, mulliganType);

                    return copy;
                })
                .ToHashSet(); // want hash set for undefined (random) iter order

            _hand = new List<CardPreview>();

            _cardsInDeck = deck
                .Sum(d => GetCopies(d, mulliganType));

            _nextDraw = PickRandomCard();
        }

        public IReadOnlyList<CardPreview> Hand => _hand;

        public bool CanDraw => _nextDraw is not null;

        public void Dispose()
        {
            foreach (var option in _cardOptions)
            {
                _cardPool.Return(option);
            }

            _cardOptions.Clear();
            _hand.Clear();

            _nextDraw = null;
        }

        public void DrawCard()
        {
            if (_nextDraw is not { Card: CardPreview card })
            {
                return;
            }

            _nextDraw.Copies -= 1;
            _cardsInDeck -= 1;

            _nextDraw = PickRandomCard(); // keep eye on, O(N) could be bottleneck

            _hand.Add(card);
        }

        private CardCopy? PickRandomCard()
        {
            if (_cardsInDeck <= 0)
            {
                return null;
            }

            int picked = Random.Shared.Next(0, _cardsInDeck);

            using var e = _cardOptions.GetEnumerator();

            while (e.MoveNext())
            {
                picked -= e.Current.Copies;

                if (picked <= 0)
                {
                    return e.Current;
                }
            }

            return null;
        }

        private static int GetCopies(DeckCopy source, MulliganType mulliganType)
        {
            return mulliganType switch
            {
                MulliganType.Built => source.Held,
                MulliganType.Theorycraft => source.Held - source.Returning + source.Want,
                _ => 0
            };
        }

        private sealed record CardCopy
        {
            public CardPreview? Card { get; set; }
            public int Copies { get; set; }
        }
    }
}
