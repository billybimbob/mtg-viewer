using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;
using MtgViewer.Utils;

namespace MtgViewer.Pages;

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

    #region View Properties

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal bool IsFirstLoad => _isBusy && _shuffleOrder.Count is 0;

    internal bool IsEmptyCollection => !_isBusy && _randomCards.Count is 0;

    internal bool IsFullyLoaded => _randomCards.Count >= _shuffleOrder.Count;

    internal IReadOnlyList<CardImage> RandomCards => _randomCards;

    internal IReadOnlyList<RecentTransaction> RecentChanges { get; private set; } = Array.Empty<RecentTransaction>();

    #endregion

    private const int ChunkSize = 4;

    private readonly CancellationTokenSource _cancel = new();

    private readonly List<CardImage> _randomCards = new();
    private readonly HashSet<string> _loadedImages = new();

    private IReadOnlyList<string> _shuffleOrder = Array.Empty<string>();
    private DateTime _currentTime;

    private PersistingComponentStateSubscription _persistSubscription;

    private bool _isBusy;
    private bool _isInteractive;

    protected override async Task OnInitializedAsync()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistCardData);

        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            RecentChanges = await GetRecentChangesAsync(dbContext);

            _shuffleOrder = await GetShuffleOrderAsync(dbContext);

            await LoadRandomCardsAsync(dbContext);

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
        ApplicationState.PersistAsJson(nameof(_shuffleOrder), _shuffleOrder);

        ApplicationState.PersistAsJson(nameof(_randomCards), _randomCards);

        ApplicationState.PersistAsJson(nameof(RecentChanges), RecentChanges);

        return Task.CompletedTask;
    }

    private async Task<IReadOnlyList<RecentTransaction>> GetRecentChangesAsync(CardDbContext dbContext)
    {
        if (ApplicationState.TryGetData(nameof(RecentChanges), out RecentTransaction[]? changes))
        {
            return changes;
        }

        return await RecentTransactionsAsync
            .Invoke(dbContext, PageSize.Current)
            .ToListAsync(_cancel.Token);
    }

    private async Task<IReadOnlyList<string>> GetShuffleOrderAsync(CardDbContext dbContext)
    {
        if (ApplicationState.TryGetData(nameof(_shuffleOrder), out string[]? shuffleOrder))
        {
            return shuffleOrder;
        }

        return await ShuffleOrderAsync
            .Invoke(dbContext, PageSize.Current)
            .ToListAsync(_cancel.Token);
    }

    private async Task LoadRandomCardsAsync(CardDbContext dbContext)
    {
        if (_randomCards.Any())
        {
            Logger.LogWarning("{Property} is already loaded", nameof(_randomCards));
            return;
        }

        if (!_shuffleOrder.Any())
        {
            Logger.LogError("Expected {Missing} is not loaded", nameof(_shuffleOrder));
            return;
        }

        if (ApplicationState.TryGetData(nameof(_randomCards), out CardImage[]? cards))
        {
            _randomCards.AddRange(cards);
            return;
        }

        await LoadNextChunkAsync(dbContext);
    }

    internal async Task LoadMoreCardsAsync()
    {
        if (_isBusy || IsFullyLoaded)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await LoadNextChunkAsync(dbContext);
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

    private async Task LoadNextChunkAsync(CardDbContext dbContext)
    {
        string[] chunk = _shuffleOrder
            .Skip(_randomCards.Count)
            .Take(ChunkSize)
            .ToArray();

        if (!chunk.Any())
        {
            Logger.LogError("Cannot load any more chunks");
            return;
        }

        var dbCards = CardChunkAsync(dbContext, chunk).WithCancellation(_cancel.Token);

        await foreach (var card in dbCards)
        {
            _randomCards.Add(card);
        }
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

    internal bool IsImageLoaded(CardImage card)
        => (_loadedImages.Count is 0 && !_isInteractive)
            || _loadedImages.Contains(card.Id);

    internal void OnImageLoad(CardImage card)
        => _loadedImages.Add(card.Id);

    #region Database Queries

    private static readonly Func<CardDbContext, int, IAsyncEnumerable<RecentTransaction>> RecentTransactionsAsync
        = EF.CompileAsyncQuery((CardDbContext db, int limit)
            => db.Transactions
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
        = EF.CompileAsyncQuery((CardDbContext db, int limit)
            => db.Cards
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
