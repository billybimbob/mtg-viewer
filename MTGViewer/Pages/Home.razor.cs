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

public partial class Home : ComponentBase, IDisposable
{
    [Inject]
    protected IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected PageSizes PageSizes { get; set; } = default!;

    [Inject]
    protected ILogger<Home> Logger { get; set; } = default!;


    public bool IsBusy => _isBusy;

    public bool IsFullyLoaded => !_randomContext?.HasMore ?? true;

    public IReadOnlyList<CardImage> RandomCards => _randomContext?.Cards ?? Array.Empty<CardImage>();

    public IReadOnlyList<RecentTransaction> RecentChanges => _recentChanges;

    public bool IsEmptyCollection => !_randomContext?.Cards.Any() ?? false;

    public bool IsImageLoaded(CardImage card) =>
        _randomContext?.LoadedImages.Contains(card.Id) ?? false;


    private const int ChunkSize = 4;

    private readonly CancellationTokenSource _cancel = new();

    private bool _isBusy;
    private DateTime _currentTime;

    private RandomCardsContext? _randomContext;
    private IReadOnlyList<RecentTransaction> _recentChanges = Array.Empty<RecentTransaction>();


    protected override async Task OnInitializedAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            var cancelToken = _cancel.Token;
            int loadLimit = PageSizes.Limit;

            await using var dbContext = await DbFactory.CreateDbContextAsync(cancelToken);

            _randomContext = await RandomCardsContext.CreateAsync(dbContext, loadLimit, cancelToken);

            _recentChanges = await RecentTransactions
                .Invoke(dbContext, loadLimit)
                .ToListAsync(cancelToken);

            _currentTime = DateTime.UtcNow;
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogError(ex.ToString());
        }
        finally
        {
            _isBusy = false;
        }
    }


    public void Dispose()
    {
        _cancel.Cancel();
        _cancel.Dispose();
    }



    public static string CardNames(IEnumerable<RecentChange> changes)
    {
        var cardNames = changes
            .GroupBy(c => c.CardName, (name, _) => name);

        return string.Join(", ", cardNames);
    }


    public string ElapsedTime(RecentTransaction transaction)
    {
        var elapsed = _currentTime - transaction.AppliedAt;

        return elapsed switch
        {
            { Days: >0 } => $"{elapsed.Days} days ago",
            { Hours: >0 } => $"{elapsed.Hours} hours ago",
            { Minutes: >0 } => $"{elapsed.Minutes} min ago",
            _ => $"{elapsed.Seconds} sec ago"
        };
    }


    public async Task LoadMoreCardsAsync()
    {
        if (_isBusy || _randomContext is null || !_randomContext.HasMore)
        {
            return;
        }

        _isBusy = true;
        
        try
        {
            var cancelToken = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(cancelToken);

            await _randomContext.LoadNextChunkAsync(dbContext, cancelToken);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogError(ex.ToString());
        }
        finally
        {
            _isBusy = false;
        }
    }


    public void CardImageLoaded(CardImage card)
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
            var loadOrder = await ShuffleOrderAsync(dbContext, loadLimit, cancel);
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

            var nextChunk = HasMore 
                ? _loadOrder[_cards.Count / ChunkSize] 
                : null;

            if (nextChunk is null)
            {
                throw new InvalidOperationException("Cannot load any more chunks");
            }

            var newCards = await CardChunkAsync(dbContext, nextChunk, cancel);

            _cards.AddRange(newCards);
        }
    }



    #region Database Queries

    private static ValueTask<List<string[]>> ShuffleOrderAsync(
        CardDbContext dbContext,
        int limit,
        CancellationToken cancel)
    {
        return ShuffleOrder(dbContext, limit)
            .Chunk(ChunkSize)
            .ToListAsync(cancel);
    }


    private static readonly Func<CardDbContext, int, IAsyncEnumerable<string>> ShuffleOrder
        = EF.CompileAsyncQuery((CardDbContext dbContext, int limit) =>
            dbContext.Cards
                .Select(c => c.Id)
                .OrderBy(_ => EF.Functions.Random())
                .Take(limit));


    private static ValueTask<List<CardImage>> CardChunkAsync(
        CardDbContext dbContext, 
        string[] chunk,
        CancellationToken cancel)
    {
        var chunkPreview = CardChunk.Invoke(dbContext, chunk);

        // preserve order of chunk
        return chunk
            .ToAsyncEnumerable()
            .Join(chunkPreview,
                cid => cid,
                c => c.Id,
                (_, preview) => preview)
            .ToListAsync(cancel);
    }


    private static readonly Func<CardDbContext, string[], IAsyncEnumerable<CardImage>> CardChunk

        = EF.CompileAsyncQuery((CardDbContext dbContext, string[] chunk) =>
            dbContext.Cards
                .Where(c => chunk.Contains(c.Id))
                .Select(c => new CardImage
                {
                    Id = c.Id,
                    Name = c.Name,
                    ImageUrl = c.ImageUrl
                }));


    private static readonly Func<CardDbContext, int, IAsyncEnumerable<RecentTransaction>> RecentTransactions
        = EF.CompileAsyncQuery((CardDbContext dbContext, int limit) =>

            dbContext.Transactions
                .Where(t => t.Changes
                    .Any(c => c.From is Box || c.To is Box
                        || c.From is Excess || c.To is Excess))

                .OrderByDescending(t => t.AppliedAt)
                .Take(ChunkSize)

                .Select(t => new RecentTransaction
                {
                    AppliedAt = t.AppliedAt,
                    Total = t.Changes.Sum(c => c.Copies),

                    Changes = t.Changes
                        .Where(c => c.From is Box || c.To is Box
                            || c.From is Excess || c.To is Excess)

                        .OrderBy(c => c.Card.Name)
                        .Take(limit)
                        .Select(c => new RecentChange
                        {
                            ToStorage = c.To is Box || c.To is Excess,
                            FromStorage = c.From is Box || c.From is Excess,
                            CardName = c.Card.Name
                        }),
                }));

    #endregion
}