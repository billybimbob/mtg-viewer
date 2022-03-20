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

namespace MTGViewer.Pages;

public sealed partial class Home : ComponentBase, IDisposable
{
    [Inject]
    internal IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    internal PageSizes PageSizes { get; set; } = default!;

    [Inject]
    internal ILogger<Home> Logger { get; set; } = default!;


    internal bool IsFirstLoad => IsBusy && _randomContext is null;

    internal bool IsEmptyCollection => _randomContext is { Cards.Count: 0 };

    internal bool IsFullyLoaded => _randomContext is null or { HasMore: false };

    internal IReadOnlyList<CardImage> RandomCards => _randomContext?.Cards ?? Array.Empty<CardImage>();

    internal IReadOnlyList<RecentTransaction> RecentChanges => _recentChanges;

    internal bool IsBusy { get; private set; }


    private const int ChunkSize = 4;

    private readonly CancellationTokenSource _cancel = new();

    private RecentTransaction[] _recentChanges = Array.Empty<RecentTransaction>();
    private RandomCardsContext? _randomContext;
    private DateTime _currentTime;


    protected override async Task OnInitializedAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var token = _cancel.Token;
            int loadLimit = PageSizes.Limit;

            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            _randomContext = await RandomCardsContext.CreateAsync(dbContext, loadLimit, token);

            _recentChanges = await RecentTransactionsAsync
                .Invoke(dbContext, loadLimit)
                .ToArrayAsync(token);

            _currentTime = DateTime.UtcNow;
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }


    public void Dispose()
    {
        _cancel.Cancel();
        _cancel.Dispose();
    }


    internal bool IsImageLoaded(CardImage card)
    {
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
        if (IsBusy || _randomContext is null or { HasMore: false })
        {
            return;
        }

        IsBusy = true;

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
            IsBusy = false;
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
        private readonly IReadOnlyList<string[]> _loadOrder;
        private readonly int _limit;

        public ICollection<string> LoadedImages => _imageLoaded;
        public IReadOnlyList<CardImage> Cards => _cards;
        public bool HasMore => _cards.Count < _limit;


        private RandomCardsContext(IReadOnlyList<string[]> loadOrder)
        {
            _cards = new();
            _imageLoaded = new();
            _loadOrder = loadOrder;
            _limit = _loadOrder.Sum(chunk => chunk.Length);
        }


        public static async Task<RandomCardsContext> CreateAsync(
            CardDbContext dbContext,
            int loadLimit,
            CancellationToken cancel)
        {
            var loadOrder = await ShuffleOrderAsync
                .Invoke(dbContext, loadLimit)
                .Chunk(ChunkSize)
                .ToListAsync(cancel);

            var randomContext = new RandomCardsContext(loadOrder);

            if (randomContext.HasMore)
            {
                await randomContext.LoadNextChunkAsync(dbContext, cancel);
            }

            return randomContext;
        }

        public async Task LoadNextChunkAsync(CardDbContext dbContext, CancellationToken cancel)
        {
            ArgumentNullException.ThrowIfNull(dbContext);

            var chunk = HasMore
                ? _loadOrder[_cards.Count / ChunkSize]
                : null;

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
