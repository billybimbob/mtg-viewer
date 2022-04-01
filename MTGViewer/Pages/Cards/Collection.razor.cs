using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;
using MTGViewer.Pages.Shared;
using MTGViewer.Services;

namespace MTGViewer.Pages.Cards;


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
    public bool Reversed { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public int Page { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public int Size { get; set; }


    [Inject]
    internal IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    internal PageSizes PageSizes { get; set; } = default!;

    [Inject]
    internal NavigationManager Nav { get; set; } = default!;

    [Inject]
    internal PersistentComponentState ApplicationState { get; set; } = default!;

    [Inject]
    internal ILogger<Collection> Logger { get; set; } = default!;


    internal event EventHandler? ParametersChanged;

    internal event EventHandler? CardsLoaded;


    internal bool IsLoading => _isBusy || !_isInteractive;

    internal FilterContext Filters { get; } = new();

    internal OffsetList<LocationCopy> Cards { get; private set; } = OffsetList<LocationCopy>.Empty;


    private readonly CancellationTokenSource _cancel = new();
    private readonly Dictionary<string, object?> _newFilters = new(4);

    private PersistingComponentStateSubscription _persistSubscription;

    private bool _isBusy;
    private bool _isInteractive;


    protected override void OnInitialized()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistCardData);

        ParametersChanged += Filters.OnParametersChanged;
        CardsLoaded += Filters.OnCardsLoaded;
    }


    protected async override Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            ParametersChanged?.Invoke(this, EventArgs.Empty);

            await LoadCardDataAsync(_cancel.Token);

            if (Cards is { Count: 0, Offset.Current: > 0 })
            {
                Logger.LogWarning("Invalid offset {Offset} was given", Cards.Offset.Current);

                Nav.NavigateTo(
                    Nav.GetUriWithQueryParameter(nameof(Page), null as int?), replace: true);

                return;
            }

            CardsLoaded?.Invoke(this, EventArgs.Empty);

            Filters.FilterChanged -= OnFilterChanged;
            Filters.FilterChanged += OnFilterChanged;
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
        ParametersChanged -= Filters.OnParametersChanged;
        CardsLoaded -= Filters.OnCardsLoaded;

        Filters.FilterChanged -= OnFilterChanged;

        _persistSubscription.Dispose();

        _cancel.Cancel();
        _cancel.Dispose();
    }


    private Task PersistCardData()
    {
        ApplicationState.PersistAsJson(nameof(Cards), Cards as IReadOnlyList<LocationCopy>);

        ApplicationState.PersistAsJson(nameof(Cards.Offset), Cards.Offset);

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


    private async Task LoadCardDataAsync(CancellationToken cancel)
    {
        if (TryGetData(nameof(Cards), out IReadOnlyList<LocationCopy>? cards)
            && TryGetData(nameof(Offset), out Offset offset))
        {
            // persisted state should match set filters
            // TODO: find way to check filters are consistent

            Cards = new OffsetList<LocationCopy>(offset, cards);
            return;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(cancel);

        Cards = await FilteredCardsAsync(dbContext, Filters, cancel);
    }


    private void OnFilterChanged(object? sender, FilterEventArgs args)
    {
        if (_isBusy || sender is not FilterContext filter)
        {
            return;
        }

        _isBusy = true;

        filter.FilterChanged -= OnFilterChanged;

        _newFilters.Clear();

        foreach (var (name, value) in args.Changes)
        {
            _newFilters.Add(name, value);
        }

        // triggers on ParameterSet, where IsBusy set to false

        Nav.NavigateTo(
            Nav.GetUriWithQueryParameters(_newFilters), replace: true);
    }



    internal sealed class FilterEventArgs : EventArgs
    {
        private readonly KeyValuePair<string, object?>[] _changes;

        public FilterEventArgs(params KeyValuePair<string, object?>[] changes)
        {
            _changes = changes;
        }

        public IEnumerable<KeyValuePair<string, object?>> Changes => _changes;
    }


    internal sealed class FilterContext
    {
        public event EventHandler<FilterEventArgs>? FilterChanged;

        private string? _searchName;
        public string? SearchName
        {
            get => _searchName;
            set
            {
                value = ValidatedSearchName(value);

                if (value == _searchName)
                {
                    return;
                }

                if (FilterChanged is null)
                {
                    return;
                }

                var args = new FilterEventArgs(
                    KeyValuePair.Create(nameof(Collection.Search), value as object),
                    KeyValuePair.Create(nameof(Collection.Page), null as object),
                    KeyValuePair.Create(nameof(Collection.Size), null as object));

                FilterChanged.Invoke(this, args);
            }
        }

        private string? ValidatedSearchName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
            }

            if (value?.Length > CardFilter.SearchNameLimit || value == _searchName)
            {
                return _searchName;
            }

            return value;
        }


        private const string DefaultOrder = nameof(Card.Name);

        public bool IsReversed { get; private set; }

        private string _orderBy = DefaultOrder;
        public string OrderBy
        {
            get => _orderBy;
            private set
            {
                bool isValid = value
                    is nameof(Card.Name)
                    or nameof(Card.ManaCost)
                    or nameof(Card.SetName)
                    or nameof(Card.Rarity)
                    or nameof(Card.Holds);

                _orderBy = isValid ? value : DefaultOrder;
            }
        }

        public void Reorder<T>(Expression<Func<Card, T>> property)
        {
            if (property is not { Body: MemberExpression { Member.Name: string value } })
            {
                return;
            }

            if (FilterChanged is null)
            {
                return;
            }

            object? reversed = value == _orderBy && !IsReversed ? true : null;

            var args = new FilterEventArgs(
                KeyValuePair.Create(nameof(Collection.Reversed), reversed),
                KeyValuePair.Create(nameof(Collection.Order), (object?)value),
                KeyValuePair.Create(nameof(Collection.Page), null as object),
                KeyValuePair.Create(nameof(Collection.Size), null as object));

            FilterChanged.Invoke(this, args);
        }


        private int _maxPage;
        public int PageSize { get; private set; }

        private int _pageIndex;
        public int PageIndex
        {
            get => _pageIndex;
            private set
            {
                if (IsValidPageIndex(value))
                {
                    _pageIndex = value;
                }
            }
        }

        public void SetPage(int value)
        {
            if (!IsValidPageIndex(value))
            {
                return;
            }

            if (FilterChanged is null)
            {
                return;
            }

            object? offset = value == default ? null : value;

            var args = new FilterEventArgs(
                KeyValuePair.Create(nameof(Collection.Page), offset),
                KeyValuePair.Create(nameof(Collection.Size), (object?)_maxPage));

            FilterChanged.Invoke(this, args);
        }

        private bool IsValidPageIndex(int value)
        {
            return value >= 0
                && value != _pageIndex
                && value < _maxPage;
        }


        public Color PickedColors { get; private set; }

        public void ToggleColor(Color value)
        {
            if (FilterChanged is null)
            {
                return;
            }

            value = PickedColors.HasFlag(value)
                ? PickedColors & ~value
                : PickedColors | value;

            var newValues = new FilterEventArgs(
                KeyValuePair.Create(nameof(Collection.Colors), (object?)(int)value),
                KeyValuePair.Create(nameof(Collection.Page), null as object),
                KeyValuePair.Create(nameof(Collection.Size), null as object));

            FilterChanged.Invoke(this, newValues);
        }


        public void OnParametersChanged(object? sender, EventArgs _)
        {
            if (sender is not Collection parameters)
            {
                return;
            }

            if (parameters.Size > 0)
            {
                _maxPage = parameters.Size;
            }

            _searchName = ValidatedSearchName(parameters.Search);

            PickedColors = (Color)parameters.Colors;

            if (PageSize == 0)
            {
                PageSize = parameters.PageSizes.GetComponentSize<Collection>();
            }

            PageIndex = parameters.Page;
            IsReversed = parameters.Reversed;
            OrderBy = parameters.Order ?? DefaultOrder;
        }


        public void OnCardsLoaded(object? sender, EventArgs _)
        {
            if (sender is not Collection parameters)
            {
                return;
            }

            _maxPage = parameters.Cards.Offset.Total;
        }
    }



    private static Task<OffsetList<LocationCopy>> FilteredCardsAsync(
        CardDbContext dbContext,
        FilterContext filters,
        CancellationToken cancel)
    {
        var cards = dbContext.Cards.AsQueryable();

        var searchName = filters.SearchName;
        var pickedColors = filters.PickedColors;

        if (!string.IsNullOrWhiteSpace(searchName))
        {
            // keep eye on perf, postgres is slow here
            cards = cards
                .Where(c => c.Name.ToLower()
                    .Contains(searchName.ToLower()));
        }

        if (pickedColors is not Color.None)
        {
            cards = cards
                .Where(c => (c.Color & pickedColors) == pickedColors);
        }

        int pageSize = filters.PageSize;
        int pageIndex = filters.PageIndex;

        return CardsOrdered(cards, filters)
            .PageBy(pageIndex, pageSize)
            .Select(c => new LocationCopy
            {
                Id = c.Id,
                Name = c.Name,
                SetName = c.SetName,
                ManaCost = c.ManaCost,
                Rarity = c.Rarity,
                ImageUrl = c.ImageUrl,
                Held = c.Holds.Sum(c => c.Copies)
            })
            .ToOffsetListAsync(cancel);
    }


    private static IQueryable<Card> CardsOrdered(IQueryable<Card> cards, FilterContext filters)
    {
        bool isAscending = filters.OrderBy switch
        {
            nameof(Card.ManaCost) => false,
            nameof(Card.SetName) => true,
            nameof(Card.Rarity) => false,
            nameof(Card.Holds) => false,
            _ => true
        };

        IOrderedQueryable<Card> PrimaryOrder<T>(Expression<Func<Card, T>> property)
        {
            return isAscending ^ filters.IsReversed
                ? cards.OrderBy(property)
                : cards.OrderByDescending(property);
        }

        return filters.OrderBy switch
        {
            nameof(Card.ManaCost) =>
                PrimaryOrder(c => c.ManaValue)
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id),

            nameof(Card.SetName) =>
                PrimaryOrder(c => c.SetName)
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.Id),

            nameof(Card.Holds) =>
                PrimaryOrder(c => c.Holds.Sum(h => h.Copies))
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id),

            nameof(Card.Rarity) =>
                PrimaryOrder(c => c.Rarity)
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id),

            nameof(Card.Name) or _ =>
                PrimaryOrder(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id)
        };
    }

}