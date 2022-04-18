using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Utils;

namespace MTGViewer.Pages;

public sealed partial class Home : ComponentBase, IDisposable
{
    [Inject]
    internal IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    internal PersistentComponentState ApplicationState { get; set; } = default!;

    [Inject]
    internal PageSize PageSize { get; set; } = default!;

    [Inject]
    internal ILogger<Home> Logger { get; set; } = default!;

    private const int ChunkSize = 4;

    private readonly CancellationTokenSource _cancel = new();

    private bool _isBusy;
    private bool _isInteractive;

    private PersistingComponentStateSubscription _persistSubscription;

    private RecentTransaction[] _recentChanges = Array.Empty<RecentTransaction>();
    private RandomCardsContext? _randomContext;
    private DateTime _currentTime;

    protected override async Task OnInitializedAsync()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistCardData);

        _isBusy = true;

        try
        {
            var token = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            _recentChanges = await GetRecentTransactionsAsync(dbContext, token);

            _randomContext = await GetRandomContextAsync(dbContext, token);

            _currentTime = DateTime.UtcNow;
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);
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

    void IDisposable.Dispose()
    {
        _persistSubscription.Dispose();

        _cancel.Cancel();
        _cancel.Dispose();
    }

    private Task PersistCardData()
    {
        ApplicationState.PersistAsJson(nameof(_randomContext.Order), _randomContext?.Order);
        ApplicationState.PersistAsJson(nameof(_randomContext.Cards), _randomContext?.Cards);

        ApplicationState.PersistAsJson(nameof(_recentChanges), _recentChanges);

        return Task.CompletedTask;
    }

    private async Task<RecentTransaction[]> GetRecentTransactionsAsync(CardDbContext dbContext, CancellationToken cancel)
    {
        if (ApplicationState.TryGetData(nameof(_recentChanges), out RecentTransaction[]? changes))
        {
            return changes;
        }

        return await RecentTransactionsAsync
            .Invoke(dbContext, PageSize.Current)
            .ToArrayAsync(cancel);
    }

    private async Task<RandomCardsContext> GetRandomContextAsync(CardDbContext dbContext, CancellationToken cancel)
    {
        if (!ApplicationState.TryGetData(nameof(_randomContext.Order), out List<string[]>? order))
        {
            return await RandomCardsContext.CreateAsync(dbContext, PageSize.Current, cancel);
        }

        if (!ApplicationState.TryGetData(nameof(_randomContext.Cards), out List<CardImage>? cards))
        {
            return await RandomCardsContext.CreateAsync(order, dbContext, cancel);
        }

        return await RandomCardsContext.CreateAsync(order, cards, dbContext, cancel);
    }

    #region View Properties

    internal bool IsFirstLoad => _isBusy && _randomContext is null;

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal bool IsEmptyCollection => _randomContext is { Cards.Count: 0 };

    internal bool IsFullyLoaded => _randomContext is null or { HasMore: false };

    internal IReadOnlyList<CardImage> RandomCards => _randomContext?.Cards ?? Array.Empty<CardImage>();

    internal IReadOnlyList<RecentTransaction> RecentChanges => _recentChanges;

    #endregion

    internal bool IsImageLoaded(CardImage card)
    {
        if (_randomContext is null)
        {
            return false;
        }

        if (_randomContext is { LoadedImages.Count: 0 } && !_isInteractive)
        {
            return true;
        }

        return _randomContext.LoadedImages.Contains(card.Id);
    }

    internal static string CardNames(IEnumerable<RecentChange> changes)
    {
        var cardNames = changes
            .GroupBy(c => c.CardName, (name, _) => name);

        return string.Join(", ", cardNames);
    }

    internal string ElapsedTime(RecentTransaction transaction)
    {
        var elapsed = _currentTime - transaction.AppliedAt;

        return elapsed switch
        {
            { Days: > 0 } => $"{elapsed.Days} days ago",
            { Hours: > 0 } => $"{elapsed.Hours} hours ago",
            { Minutes: > 0 } => $"{elapsed.Minutes} min ago",
            _ => $"{elapsed.Seconds} sec ago"
        };
    }

    internal void OnImageLoad(CardImage card)
    {
        _randomContext?.OnImageLoad(card);
    }

    internal async Task LoadMoreCardsAsync()
    {
        if (_isBusy || _randomContext is null or { HasMore: false })
        {
            return;
        }

        _isBusy = true;

        try
        {
            var token = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            await _randomContext.LoadNextChunkAsync(dbContext, token);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private sealed class RandomCardsContext
    {
        private readonly List<CardImage> _cards;
        private readonly HashSet<string> _imageLoaded;
        private readonly int _limit;

        public IReadOnlyList<string[]> Order { get; }

        public IReadOnlyCollection<string> LoadedImages => _imageLoaded;

        public IReadOnlyList<CardImage> Cards => _cards;

        public bool HasMore => _cards.Count < _limit;

        private RandomCardsContext(IReadOnlyList<string[]> loadOrder, List<CardImage> cards)
        {
            _cards = cards;
            _limit = loadOrder.Sum(chunk => chunk.Length);
            _imageLoaded = new HashSet<string>();

            Order = loadOrder;
        }

        public static async Task<RandomCardsContext> CreateAsync(
            CardDbContext dbContext,
            int limit,
            CancellationToken cancel)
        {
            var loadOrder = await ShuffleOrderAsync
                .Invoke(dbContext, limit)
                .Chunk(ChunkSize)
                .ToListAsync(cancel);

            return await CreateAsync(loadOrder, dbContext, cancel);
        }

        public static async Task<RandomCardsContext> CreateAsync(
            IReadOnlyList<string[]> loadOrder,
            CardDbContext dbContext,
            CancellationToken cancel)
        {
            var randomContext = new RandomCardsContext(loadOrder, new List<CardImage>());

            if (randomContext.HasMore)
            {
                await randomContext.LoadNextChunkAsync(dbContext, cancel);
            }

            return randomContext;
        }

        public static async Task<RandomCardsContext> CreateAsync(
            IReadOnlyList<string[]> loadOrder,
            List<CardImage> cards,
            CardDbContext dbContext,
            CancellationToken cancel)
        {
            if (!AreValidCards(loadOrder, cards))
            {
                cards.Clear();
            }

            var randomContext = new RandomCardsContext(loadOrder, cards);

            if (randomContext is { Cards.Count: 0, HasMore: true })
            {
                await randomContext.LoadNextChunkAsync(dbContext, cancel);
            }

            return randomContext;
        }

        private static bool AreValidCards(IReadOnlyList<string[]> loadOrder, IReadOnlyList<CardImage> cards)
        {
            var expectedOrder = loadOrder
                .SelectMany(chunk => chunk)
                .Take(cards.Count);

            return cards
                .Select(c => c.Id)
                .SequenceEqual(expectedOrder);
        }

        public async Task LoadNextChunkAsync(CardDbContext dbContext, CancellationToken cancel)
        {
            ArgumentNullException.ThrowIfNull(dbContext);

            string[]? chunk = Order.ElementAtOrDefault(_cards.Count / ChunkSize);

            if (chunk is null)
            {
                throw new InvalidOperationException("Cannot load any more chunks");
            }

            var newCards = await CardChunkAsync(dbContext, chunk).ToListAsync(cancel);

            _cards.AddRange(newCards);
        }

        public void OnImageLoad(CardImage card)
        {
            if (card?.Id is string cardId)
            {
                _ = _imageLoaded.Add(cardId);
            }
        }
    }

    #region Database Queries

    private static readonly Func<CardDbContext, int, IAsyncEnumerable<RecentTransaction>> RecentTransactionsAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int limit) =>
            dbContext.Transactions
                .Where(t => t.Changes
                    .Any(c => c.From is Box
                        || c.From is Excess
                        || c.To is Box
                        || c.To is Excess))

                .OrderByDescending(t => t.AppliedAt)
                .Take(ChunkSize)
                .Select(t => new RecentTransaction
                {
                    AppliedAt = t.AppliedAt,
                    Copies = t.Changes.Sum(c => c.Copies),

                    Changes = t.Changes
                        .Where(c => c.From is Box
                            || c.From is Excess
                            || c.To is Box
                            || c.To is Excess)

                        .OrderBy(c => c.Card.Name)
                        .Take(limit)
                        .Select(c => new RecentChange
                        {
                            FromStorage = c.From is Box || c.From is Excess,
                            ToStorage = c.To is Box || c.To is Excess,
                            CardName = c.Card.Name,
                        }),
                }));

    private static readonly Func<CardDbContext, int, IAsyncEnumerable<string>> ShuffleOrderAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int limit) =>
            dbContext.Cards
                .Select(c => c.Id)
                .OrderBy(_ => EF.Functions.Random())
                .Take(limit));

    private static IAsyncEnumerable<CardImage> CardChunkAsync(CardDbContext dbContext, string[] chunk)
    {
        var dbChunk = dbContext.Cards
            .Where(c => chunk.Contains(c.Id))
            .Select(c => new CardImage
            {
                Id = c.Id,
                Name = c.Name,
                ImageUrl = c.ImageUrl
            })
            .AsAsyncEnumerable();

        // preserve order of chunk
        return chunk
            .ToAsyncEnumerable()
            .Join(dbChunk,
                cid => cid, c => c.Id,
                (_, preview) => preview);
    }

    #endregion
}
