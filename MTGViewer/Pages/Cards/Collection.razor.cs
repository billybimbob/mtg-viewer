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
    public string? SearchName { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public int PickedColors { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? OrderBy { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public bool Reversed { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public int Offset { get; set; }

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


    public event EventHandler? ParametersChanged;
    public event EventHandler? CardsLoaded;

    public bool IsBusy => _isBusy;

    public FilterContext Filters => _filters;

    public OffsetList<LocationCopy> Cards => _cards ?? OffsetList<LocationCopy>.Empty;



    private bool _isBusy;
    private readonly CancellationTokenSource _cancel = new();

    private readonly FilterContext _filters = new();
    private readonly Dictionary<string, object?> _newFilters = new(3);

    private OffsetList<LocationCopy>? _cards;


    protected override void OnInitialized()
    {
        ParametersChanged += _filters.OnParametersChanged;
        CardsLoaded += _filters.OnCardsLoaded;
    }


    protected async override Task OnParametersSetAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            ParametersChanged?.Invoke(this, EventArgs.Empty);

            var token = _cancel.Token;
            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            _cards = await FilteredCardsAsync(dbContext, _filters, token);

            if (_cards is { Count: 0, Offset.Current: >0})
            {
                Nav.NavigateTo(
                    Nav.GetUriWithQueryParameter(nameof(Offset), null as bool?), replace: true);
                return;
            }

            CardsLoaded?.Invoke(this, EventArgs.Empty);

            _filters.FilterChanged -= OnFilterChanged;
            _filters.FilterChanged += OnFilterChanged;
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


    public void Dispose()
    {
        ParametersChanged -= _filters.OnParametersChanged;
        CardsLoaded -= _filters.OnCardsLoaded;

        _filters.FilterChanged -= OnFilterChanged;

        _cancel.Cancel();
        _cancel.Dispose();
    }


    private async void OnFilterChanged(object? sender, FilterEventArgs args)
    {
        if (_isBusy || sender is not FilterContext filter)
        {
            return;
        }

        _isBusy = true;

        try
        {
            filter.FilterChanged -= OnFilterChanged;

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
            _isBusy = false;
        }
    }


    public sealed class FilterEventArgs : EventArgs
    {
        private readonly KeyValuePair<string, object?>[] _changes;

        public FilterEventArgs(params KeyValuePair<string, object?>[] changes)
        {
            _changes = changes;
        }

        public IEnumerable<KeyValuePair<string, object?>> Changes => _changes;
    }


    public sealed class FilterContext
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
                    KeyValuePair.Create(nameof(Collection.SearchName), (object?)value),
                    KeyValuePair.Create(nameof(Collection.Offset), (object?)null));

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

        public string OrderBy { get; private set; } = DefaultOrder;


        public void Reorder<T>(Expression<Func<Card, T>> property)
        {
            if (property is not { Body: MemberExpression { Member.Name: string value }})
            {
                return;
            }

            object? newReversed = value == OrderBy && !IsReversed ? true : null;

            var args = new FilterEventArgs(
                KeyValuePair.Create(nameof(Collection.Reversed), newReversed),
                KeyValuePair.Create(nameof(Collection.OrderBy), (object?)value),
                KeyValuePair.Create(nameof(Collection.Offset), (object?)null));

            FilterChanged?.Invoke(this, args);
        }


        private int _maxPage;
        public int PageSize { get; private set; }


        private int _pageIndex;
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                value = ValidatedPageIndex(value);

                if (value == _pageIndex)
                {
                    return;
                }

                object? offset = value == default ? null : value;

                var args = new FilterEventArgs(
                    KeyValuePair.Create(nameof(Collection.Offset), offset),
                    KeyValuePair.Create(nameof(Collection.Size), (object?)_maxPage));

                FilterChanged?.Invoke(this, args);
            }
        }

        private int ValidatedPageIndex(int value)
        {
            if (value >= 0 && value != _pageIndex && value < _maxPage)
            {
                return value;
            }

            return _pageIndex;
        }


        public Color PickedColors { get; private set; }

        public void ToggleColor(Color value)
        {
            value = PickedColors.HasFlag(value)
                ? PickedColors & ~value
                : PickedColors | value;

            var newValues = new FilterEventArgs(
                KeyValuePair.Create(nameof(Collection.PickedColors), (object?)(int)value),
                KeyValuePair.Create(nameof(Collection.Offset), (object?)null));

            FilterChanged?.Invoke(this, newValues);
        }


        public void OnParametersChanged(object? sender, EventArgs _)
        {
            if (sender is not Collection parameters)
            {
                return;
            }

            if (PageSize == 0)
            {
                PageSize = parameters.PageSizes.GetComponentSize<Collection>();
            }

            IsReversed = parameters.Reversed;
            OrderBy = parameters.OrderBy ?? DefaultOrder;

            PickedColors = (Color)parameters.PickedColors;

            _maxPage = Math.Max(parameters.Size, 0);

            _pageIndex = ValidatedPageIndex(parameters.Offset);
            _searchName = ValidatedSearchName(parameters.SearchName);
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