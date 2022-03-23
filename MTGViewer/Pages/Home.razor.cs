using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages;

public sealed partial class Home : ComponentBase, IDisposable
{
    [Inject]
    internal IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    internal PersistentComponentState ApplicationState { get; set; } = default!;

    [Inject]
    internal PageSizes PageSizes { get; set; } = default!;

    [Inject]
    internal ILogger<Home> Logger { get; set; } = default!;


    internal bool IsFirstLoad => _isBusy && _randomContext is null;

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal bool IsEmptyCollection => _randomContext is { Cards.Count: 0 };

    internal bool IsFullyLoaded => _randomContext is null or { HasMore: false };


    internal IReadOnlyList<CardImage> RandomCards => _randomContext?.Cards ?? Array.Empty<CardImage>();

    internal IReadOnlyList<RecentTransaction> RecentChanges => _recentChanges;


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
            var cachedLoad = GetValueOrDefault<List<string[]>>(nameof(_randomContext.Order));
            var cachedCards = GetValueOrDefault<List<CardImage>>(nameof(_randomContext.Cards));

            var cachedChanges = GetValueOrDefault<RecentTransaction[]>(nameof(_recentChanges));

            int limit = PageSizes.Limit;
            var token = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            _randomContext = await RandomCardsContext.CreateAsync(cachedLoad, cachedCards, dbContext, limit, token);

            _recentChanges = cachedChanges is not null
                ? cachedChanges
                : await RecentTransactionsAsync
                    .Invoke(dbContext, limit)
                    .ToArrayAsync(token);

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


    private TData? GetValueOrDefault<TData>(string key)
    {
        if (ApplicationState.TryTakeFromJson<TData>(key, out var data))
        {
            return data;
        }

        return default;
    }


    internal bool IsImageLoaded(CardImage card)
    {
        if (!_isInteractive)
        {
            return true;
        }

        return _randomContext?.LoadedImages.Contains(card.Id) ?? false;
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


    internal void CardImageLoaded(CardImage card)
    {
        if (_randomContext is not null && card?.Id is string cardId)
        {
            _randomContext.LoadedImages.Add(cardId);
        }
    }



    private class RandomCardsContext
    {
        private readonly List<CardImage> _cards;
        private readonly HashSet<string> _imageLoaded;
        private readonly int _limit;

        public IReadOnlyList<string[]> Order { get; }

        public ICollection<string> LoadedImages => _imageLoaded;

        public IReadOnlyList<CardImage> Cards => _cards;

        public bool HasMore => _cards.Count < _limit;


        private RandomCardsContext(IReadOnlyList<string[]> loadOrder, List<CardImage> cards)
        {
            Order = loadOrder;
            _cards = cards;

            _limit = Order.Sum(chunk => chunk.Length);
            _imageLoaded = new();
        }

        private RandomCardsContext(IReadOnlyList<string[]> loadOrder)
            : this(loadOrder, new())
        { }


        public static async Task<RandomCardsContext> CreateAsync(
            IReadOnlyList<string[]>? loadOrder,
            List<CardImage>? cards,
            CardDbContext dbContext,
            int limit,
            CancellationToken cancel)
        {
            if (loadOrder is null)
            {
                loadOrder = await ShuffleOrderAsync
                    .Invoke(dbContext, limit)
                    .Chunk(ChunkSize)
                    .ToListAsync(cancel);
            }

            var randomContext = AreValidCards(loadOrder, cards)
                ? new RandomCardsContext(loadOrder, cards)
                : new RandomCardsContext(loadOrder);

            if (randomContext is { Cards.Count: 0, HasMore: true })
            {
                await randomContext.LoadNextChunkAsync(dbContext, cancel);
            }

            return randomContext;
        }


        private static bool AreValidCards(
            IReadOnlyList<string[]> loadOrder,
            [NotNullWhen(returnValue: true)] List<CardImage>? cards)
        {
            if (cards is null)
            {
                return false;
            }

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

            var chunk = Order.ElementAtOrDefault(_cards.Count / ChunkSize);

            if (chunk is null)
            {
                throw new InvalidOperationException("Cannot load any more chunks");
            }

            var newCards = await CardChunkAsync(dbContext, chunk).ToListAsync(cancel);

            _cards.AddRange(newCards);
        }
    }



    #region Database Queries


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

    #endregion
}
