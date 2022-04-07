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
    public string? Seek { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public int Direction { get; set; }


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


    internal bool IsLoading => _isBusy || !_isInteractive;

    internal FilterContext Filters { get; } = new();

    internal SeekList<LocationCopy> Cards { get; private set; } = SeekList<LocationCopy>.Empty;


    private readonly CancellationTokenSource _cancel = new();
    private readonly Dictionary<string, object?> _newFilters = new(4);

    private PersistingComponentStateSubscription _persistSubscription;

    private bool _isBusy;
    private bool _isInteractive;


    protected override void OnInitialized()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistCardData);

        ParametersChanged += Filters.OnParametersChanged;
    }


    protected async override Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            ParametersChanged?.Invoke(this, EventArgs.Empty);

            await LoadCardDataAsync(_cancel.Token);

            if (!Cards.Any() && Seek is not null)
            {
                Logger.LogWarning("Invalid seek {Seek} was given", Seek);

                Nav.NavigateTo(
                    Nav.GetUriWithQueryParameter(nameof(Seek), null as string), replace: true);

                return;
            }

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

        Filters.FilterChanged -= OnFilterChanged;

        _persistSubscription.Dispose();

        _cancel.Cancel();
        _cancel.Dispose();
    }


    private Task PersistCardData()
    {
        ApplicationState.PersistAsJson(nameof(Cards), Cards as IReadOnlyList<LocationCopy>);

        ApplicationState.PersistAsJson(nameof(Seek), Cards.Seek);

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
            && TryGetData(nameof(Seek), out Seek<LocationCopy> seek))
        {
            // persisted state should match set filters
            // TODO: find way to check filters are consistent

            Cards = new SeekList<LocationCopy>(seek, cards);
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
                    KeyValuePair.Create(nameof(Collection.Seek), null as object),
                    KeyValuePair.Create(nameof(Collection.Direction), null as object));

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

            if (FilterChanged is null || _orderBy == value)
            {
                return;
            }

            var args = new FilterEventArgs(
                KeyValuePair.Create(nameof(Collection.Order), (object?)value),
                KeyValuePair.Create(nameof(Collection.Seek), null as object),
                KeyValuePair.Create(nameof(Collection.Direction), null as object));

            FilterChanged.Invoke(this, args);
        }


        public int PageSize { get; private set; }

        public string? Seek { get; private set; }

        public SeekDirection Direction { get; private set; }


        public void SeekPage(SeekRequest<LocationCopy> request)
        {
            if (FilterChanged is null)
            {
                return;
            }

            var direction = request.Direction is SeekDirection.Backwards
                ? (int?)SeekDirection.Backwards : null;

            var args = new FilterEventArgs(
                KeyValuePair.Create(nameof(Collection.Seek), request.Seek?.Id as object),
                KeyValuePair.Create(nameof(Collection.Direction), direction as object));

            FilterChanged.Invoke(this, args);
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

            var color = value is Color.None ? null : (int?)value;

            var newValues = new FilterEventArgs(
                KeyValuePair.Create(nameof(Collection.Colors), color as object),
                KeyValuePair.Create(nameof(Collection.Seek), null as object),
                KeyValuePair.Create(nameof(Collection.Direction), null as object));

            FilterChanged.Invoke(this, newValues);
        }


        public void OnParametersChanged(object? sender, EventArgs _)
        {
            if (sender is not Collection parameters)
            {
                return;
            }

            _searchName = ValidatedSearchName(parameters.Search);

            PickedColors = (Color)parameters.Colors;

            if (PageSize == 0)
            {
                PageSize = parameters.PageSizes.GetComponentSize<Collection>();
            }

            OrderBy = parameters.Order ?? DefaultOrder;

            Seek = parameters.Seek;
            Direction = (SeekDirection)parameters.Direction;
        }
    }



    private static Task<SeekList<LocationCopy>> FilteredCardsAsync(
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

        var copies = cards
            .Select(c => new LocationCopy
            {
                Id = c.Id,
                Name = c.Name,

                ManaCost = c.ManaCost,
                ManaValue = c.ManaValue,

                SetName = c.SetName,
                Rarity = c.Rarity,
                ImageUrl = c.ImageUrl,

                Held = c.Holds.Sum(c => c.Copies)
            });

        return OrderedCopies(copies, filters.OrderBy)
            .SeekBy(filters.Seek, filters.Direction)
            .Take(filters.PageSize)
            .ToSeekListAsync(cancel);
    }


    private static IQueryable<LocationCopy> OrderedCopies(IQueryable<LocationCopy> copies, string orderBy)
    {
        bool isAscending = orderBy switch
        {
            nameof(Card.ManaCost) => false,
            nameof(Card.SetName) => true,
            nameof(Card.Rarity) => false,
            nameof(Card.Holds) => false,
            _ => true
        };

        IOrderedQueryable<LocationCopy> PrimaryOrder<T>(Expression<Func<LocationCopy, T>> property)
        {
            return isAscending
                ? copies.OrderBy(property)
                : copies.OrderByDescending(property);
        }

        return orderBy switch
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
                PrimaryOrder(c => c.Held) // keep eye on, query is a bit expensive
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