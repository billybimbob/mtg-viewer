using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

using MtgViewer.Data.Access;
using MtgViewer.Data.Projections;
using MtgViewer.Utils;

namespace MtgViewer.Pages;

public sealed partial class Home : ComponentBase, IDisposable
{
    [Inject]
    public ICardRepository CardRepository { get; set; } = default!;

    [Inject]
    public ILedger Ledger { get; set; } = default!;

    [Inject]
    internal PersistentComponentState ApplicationState { get; set; } = default!;

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
        _isBusy = true;

        try
        {
            _persistSubscription = ApplicationState.RegisterOnPersisting(PersistCardData);

            RecentChanges = await GetRecentChangesAsync();

            _shuffleOrder = await GetShuffleOrderAsync();

            await LoadRandomCardsAsync();

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

    private async Task<IReadOnlyList<RecentTransaction>> GetRecentChangesAsync()
    {
        if (ApplicationState.TryGetData(nameof(RecentChanges), out RecentTransaction[]? changes))
        {
            return changes;
        }
        else
        {
            return await Ledger.GetRecentChangesAsync(ChunkSize, _cancel.Token);
        }
    }

    private async Task<IReadOnlyList<string>> GetShuffleOrderAsync()
    {
        if (ApplicationState.TryGetData(nameof(_shuffleOrder), out IReadOnlyList<string>? shuffleOrder))
        {
            return shuffleOrder;
        }
        else
        {

            return await CardRepository.GetShuffleOrderAsync(_cancel.Token);
        }
    }

    private async Task LoadRandomCardsAsync()
    {
        if (ApplicationState.TryGetData(nameof(_randomCards), out IReadOnlyList<CardImage>? cards))
        {
            _randomCards.AddRange(cards);
        }
        else
        {
            await LoadNextChunkAsync();
        }
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
            await LoadNextChunkAsync();
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

    private async Task LoadNextChunkAsync()
    {
        string[] chunk = _shuffleOrder
            .Skip(_randomCards.Count)
            .Take(ChunkSize)
            .ToArray();

        if (chunk.Any())
        {
            _randomCards.AddRange(
                await CardRepository.GetCardImagesAsync(chunk, _cancel.Token));
        }
    }

    internal static string CardNames(IEnumerable<RecentChange> changes)
        => changes
            .GroupBy(c => c.CardName, (name, _) => name)
            .Join(", ");

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
}
