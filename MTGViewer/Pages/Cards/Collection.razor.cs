using System;
using System.Collections.Generic;
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


public partial class Collection : ComponentBase, IDisposable
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
    protected IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected PageSizes PageSizes { get; set; } = default!;

    [Inject]
    protected NavigationManager Nav { get; set; } = default!;

    [Inject]
    protected ILogger<Collection> Logger { get; set; } = default!;


    internal event EventHandler? ParametersChanged;
    internal event EventHandler? CardsLoaded;


    internal FilterContext Filters { get; } = new();

    internal OffsetList<LocationCopy> Cards { get; private set; } = OffsetList<LocationCopy>.Empty;

    internal bool IsBusy { get; private set; }


    private readonly CancellationTokenSource _cancel = new();
    private readonly Dictionary<string, object?> _newFilters = new(3);


    protected override void OnInitialized()
    {
        ParametersChanged += Filters.OnParametersChanged;
        CardsLoaded += Filters.OnCardsLoaded;
    }


    protected async override Task OnParametersSetAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            ParametersChanged?.Invoke(this, EventArgs.Empty);

            var token = _cancel.Token;
            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            Cards = await FilteredCardsAsync(dbContext, Filters, token);

            if (Cards is { Count: 0, Offset.Current: >0})
            {
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
        finally
        {
            IsBusy = false;
        }
    }


    public void Dispose()
    {
        ParametersChanged -= Filters.OnParametersChanged;
        CardsLoaded -= Filters.OnCardsLoaded;

        Filters.FilterChanged -= OnFilterChanged;

        _cancel.Cancel();
        _cancel.Dispose();
    }


    private async void OnFilterChanged(object? sender, FilterEventArgs args)
    {
        if (IsBusy || sender is not FilterContext filter)
        {
            return;
        }

        IsBusy = true;

        try
        {
            filter.FilterChanged -= OnFilterChanged;

            // rerender should trigger at the Yield
            // NavigateTo should trigger the OnParametersSet event
            // and another render will occur after OnParameterSet

            await Task.Yield();

            _newFilters.Clear();

            foreach (var (name, value) in args.Changes)
            {
                _newFilters.Add(name, value);
            }

            Nav.NavigateTo(
                Nav.GetUriWithQueryParameters(_newFilters), replace: true);
        }
        catch (Exception ex)
        {
            Logger.LogError("{Error}", ex);
        }
        finally
        {
            IsBusy = false;
        }
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

                var args = new FilterEventArgs(
                    KeyValuePair.Create(nameof(Collection.Search), value as object),
                    KeyValuePair.Create(nameof(Collection.Page), null as object));

                FilterChanged?.Invoke(this, args);
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
            if (property is not { Body: MemberExpression { Member.Name: string value }})
            {
                return;
            }

            object? reversed = value == _orderBy && !IsReversed ? true : null;

            var args = new FilterEventArgs(
                KeyValuePair.Create(nameof(Collection.Reversed), reversed),
                KeyValuePair.Create(nameof(Collection.Order), (object?)value),
                KeyValuePair.Create(nameof(Collection.Page), null as object));

            FilterChanged?.Invoke(this, args);
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

            object? offset = value == default ? null : value;

            var args = new FilterEventArgs(
                KeyValuePair.Create(nameof(Collection.Page), offset),
                KeyValuePair.Create(nameof(Collection.Size), (object?)_maxPage));

            FilterChanged?.Invoke(this, args);
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
            value = PickedColors.HasFlag(value)
                ? PickedColors & ~value
                : PickedColors | value;

            var newValues = new FilterEventArgs(
                KeyValuePair.Create(nameof(Collection.Colors), (object?)(int)value),
                KeyValuePair.Create(nameof(Collection.Page), null as object));

            FilterChanged?.Invoke(this, newValues);
        }


        public void OnParametersChanged(object? sender, EventArgs _)
        {
            if (sender is not Collection parameters)
            {
                return;
            }

            _maxPage = Math.Max(parameters.Size, 0);
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