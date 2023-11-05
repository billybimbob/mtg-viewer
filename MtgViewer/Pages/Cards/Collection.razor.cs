using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

using MtgViewer.Data;
using MtgViewer.Data.Access;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;
using MtgViewer.Services;
using MtgViewer.Utils;

namespace MtgViewer.Pages.Cards;

public sealed partial class Collection : ComponentBase, IDisposable
{
    [Parameter]
    [SupplyParameterFromQuery]
    public string? Search { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public int Colors { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? Order { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? Seek { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public int Direction { get; set; }

    [Inject]
    internal ICardRepository CardRepository { get; set; } = default!;

    [Inject]
    internal NavigationManager Nav { get; set; } = default!;

    [Inject]
    internal PersistentComponentState ApplicationState { get; set; } = default!;

    [Inject]
    internal ILogger<Collection> Logger { get; set; } = default!;

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal SeekList<CardCopy> Cards { get; private set; } = SeekList.Empty<CardCopy>();

    private readonly CancellationTokenSource _cancel = new();
    private PersistingComponentStateSubscription _persistSubscription;

    private bool _isBusy;
    private bool _isInteractive;

    protected override void OnInitialized()
        => _persistSubscription = ApplicationState.RegisterOnPersisting(PersistCardData);

    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            Cards = await GetCardDataAsync();

            if (Cards.Count == 0 && Seek is not null)
            {
                Logger.LogWarning("Invalid seek {Seek} was given", Seek);

                Nav.NavigateTo(
                    Nav.GetUriWithQueryParameter(
                        nameof(Seek), null as string), replace: true);
            }
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);
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

    void IDisposable.Dispose()
    {
        _persistSubscription.Dispose();

        _cancel.Cancel();
        _cancel.Dispose();
    }

    private Task PersistCardData()
    {
        ApplicationState.PersistAsJson(nameof(Cards), Cards.ToSeekResponse());

        return Task.CompletedTask;
    }

    private async Task<SeekList<CardCopy>> GetCardDataAsync()
    {
        if (ApplicationState.TryGetData(nameof(Cards), out SeekResponse<CardCopy>? persistedCards))
        {
            // persisted state should match set filters
            // TODO: find way to check filters are consistent

            return persistedCards.ToSeekList();
        }

        var collectionFilter = new CollectionFilter
        {
            Search = Search,
            Colors = PickedColors,
            Order = Order,
            Seek = Seek,
            Direction = (SeekDirection)Direction
        };

        var fetchedCards = await CardRepository.GetCardCopiesAsync(collectionFilter, _cancel.Token);

        return fetchedCards.ToSeekList();
    }

    #region Change Filter Handlers

    internal string? BoundSearch
    {
        get => Search;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
            }

            const StringComparison ignoreCase = StringComparison.CurrentCultureIgnoreCase;

            if (_isBusy
                || value is { Length: > TextFilter.Limit }
                || string.Equals(value, Search, ignoreCase))
            {
                return;
            }

            _isBusy = true;

            var changes = new Dictionary<string, object?>
            {
                [nameof(Search)] = value,
                [nameof(Seek)] = null,
                [nameof(Direction)] = null
            };

            Nav.NavigateTo(
                Nav.GetUriWithQueryParameters(changes), replace: true);
        }
    }

    internal Color PickedColors
    {
        get => (Color)Colors & Symbol.Rainbow;
        set
        {
            if (_isBusy)
            {
                return;
            }

            _isBusy = true;

            var changes = new Dictionary<string, object?>
            {
                [nameof(Colors)] = value is Color.None ? null : (int)value,
                [nameof(Seek)] = null,
                [nameof(Direction)] = null
            };

            Nav.NavigateTo(
                Nav.GetUriWithQueryParameters(changes), replace: true);
        }
    }

    internal void Reorder<T>(Expression<Func<Card, T>> property)
    {
        if (property is not { Body: MemberExpression { Member.Name: string value } })
        {
            return;
        }

        if (_isBusy || Order == value)
        {
            return;
        }

        _isBusy = true;

        var changes = new Dictionary<string, object?>
        {
            [nameof(Order)] = value,
            [nameof(Seek)] = null,
            [nameof(Direction)] = null
        };

        Nav.NavigateTo(
            Nav.GetUriWithQueryParameters(changes), replace: true);
    }

    internal void SeekPage(SeekRequest<CardCopy> value)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        var changes = new Dictionary<string, object?>
        {
            [nameof(Seek)] = value.Origin?.Id,
            [nameof(Direction)] = value.Direction switch
            {
                SeekDirection.Backwards => (int)SeekDirection.Backwards,
                SeekDirection.Forward or _ => null
            }
        };

        Nav.NavigateTo(
            Nav.GetUriWithQueryParameters(changes), replace: true);
    }

    #endregion
}
