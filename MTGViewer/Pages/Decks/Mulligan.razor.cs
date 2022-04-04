using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

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
    protected PageSizes PageSizes { get; set; } = default!;

    [Inject]
    protected ILogger<Mulligan> Logger { get; set; } = default!;


    internal string DeckName => _deckName ?? "Deck Mulligan";

    internal IReadOnlyList<CardPreview> DrawnCards => _drawnCards;

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

                DrawStartingHand();
            }
        }
    }


    private const int StartingHand = 7;

    private readonly CancellationTokenSource _cancel = new();

    private bool _isBusy;
    private bool _isInteractive;

    private PersistingComponentStateSubscription _persistSubscription;

    private string? _deckName;
    private IReadOnlyList<DeckCopy> _cards = Array.Empty<DeckCopy>();

    private MulliganType _mulliganType;
    private DrawSimulation? _shuffledDeck;

    private readonly List<CardPreview> _drawnCards = new();
    private readonly HashSet<string> _loadedImages = new();


    protected override void OnInitialized()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistDeckData);
    }


    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            var token = _cancel.Token;

            var userId = await GetUserIdAsync(token);

            if (userId is null)
            {
                Nav.NavigateTo("/Cards", forceLoad: true, replace: true);
                return;
            }

            await LoadMulliganDataAsync(userId, token);

            if (_deckName is null)
            {
                Nav.NavigateTo("/Decks", forceLoad: true, replace: true);
                return;
            }

            if (!_cards.Any())
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
        ApplicationState.PersistAsJson(nameof(_deckName), _deckName);

        ApplicationState.PersistAsJson(nameof(_cards), _cards);

        return Task.CompletedTask;
    }


    private bool TryGetData<TData>(string key, [NotNullWhen(true)] out TData? data)
    {
        if (ApplicationState.TryTakeFromJson(key, out data!)
            && data is not null)
        {
            return true;
        }

        return false;
    }


    private async Task LoadMulliganDataAsync(string userId, CancellationToken cancel)
    {
        if (TryGetData(nameof(_deckName), out string? name)
            && TryGetData(nameof(_cards), out DeckCopy[]? cards))
        {
            _deckName = name;
            _cards = cards;
            return;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(cancel);

        _deckName = await dbContext.Decks
            .Where(d => d.Id == DeckId && d.OwnerId == userId)
            .Select(d => d.Name)
            .SingleOrDefaultAsync(cancel);

        _cards = await DeckCardsAsync
            .Invoke(dbContext, DeckId, PageSizes.Limit)
            .ToListAsync(cancel);
    }


    private Func<CardDbContext, int, int, IAsyncEnumerable<DeckCopy>> DeckCardsAsync

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


    private void DrawStartingHand()
    {
        _shuffledDeck?.Dispose();

        _drawnCards.Clear();
        _loadedImages.Clear();

        if (_mulliganType is MulliganType.None)
        {
            _shuffledDeck = null;
            return;
        }

        _shuffledDeck = new DrawSimulation(_cards, _mulliganType);

        int requiredCards = StartingHand;

        while (requiredCards > 0 && _shuffledDeck.CanDraw)
        {
            _drawnCards.Add(
                _shuffledDeck.DrawCard());

            requiredCards -= 1;
        }
    }


    internal void DrawCard()
    {
        if (!_isBusy
            && _isInteractive
            && _shuffledDeck is { CanDraw: true })
        {
            _drawnCards.Add(
                _shuffledDeck.DrawCard());
        }
    }


    internal void NewHand()
    {
        if (!_isBusy
            && _isInteractive
            && _drawnCards is { Count: > 0 })
        {
            DrawStartingHand();
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
        private sealed record CardCopy
        {
            public CardPreview? Card { get; set; }
            public int Copies { get; set; }
        }

        private static readonly ObjectPool<CardCopy> _cardPool
            = new DefaultObjectPool<CardCopy>(
                new DefaultPooledObjectPolicy<CardCopy>());

        private static readonly Random _random = new();

        private readonly ICollection<CardCopy> _cardOptions;

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
                // want hash set for undefined (random) iter order
                .ToHashSet();

            _cardsInDeck = deck
                .Sum(d => GetCopies(d, mulliganType));

            _nextDraw = PickRandomCard();
        }


        public bool CanDraw => _nextDraw is not null;


        public CardPreview DrawCard()
        {
            if (_nextDraw is not CardCopy { Card: CardPreview card })
            {
                throw new InvalidOperationException("There are no cards to draw");
            }

            _nextDraw.Copies -= 1;
            _cardsInDeck -= 1;

            _nextDraw = PickRandomCard(); // keep eye on, O(N) could be bottleneck

            return card;
        }


        public void Dispose()
        {
            foreach (var option in _cardOptions)
            {
                _cardPool.Return(option);
            }

            _cardOptions.Clear();

            _nextDraw = null;
        }


        private CardCopy? PickRandomCard()
        {
            if (_cardsInDeck <= 0)
            {
                return null;
            }

            int pickedIndex = _random.Next(0, _cardsInDeck);

            return FindCopy(_cardOptions, pickedIndex) switch
            {
                CardCopy c => c,
                _ => null
            };
        }


        public static int GetCopies(DeckCopy source, MulliganType mulliganType)
        {
            return mulliganType switch
            {
                MulliganType.Built => source.Held,
                MulliganType.Theorycraft => source.Held - source.Returning + source.Want,
                _ => 0
            };
        }


        private static CardCopy? FindCopy(IEnumerable<CardCopy> cards, int picked)
        {
            using var e = cards.GetEnumerator();

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
    }
}