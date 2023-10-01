using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MtgViewer.Data;
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
    internal IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    internal PageSize PageSize { get; set; } = default!;

    [Inject]
    internal ParseTextFilter ParseTextFilter { get; set; } = default!;

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
        ApplicationState.PersistAsJson(nameof(Cards), Cards as IReadOnlyList<CardCopy>);
        ApplicationState.PersistAsJson(nameof(Seek), SeekDto.From(Cards.Seek));

        return Task.CompletedTask;
    }

    private async Task<SeekList<CardCopy>> GetCardDataAsync()
    {
        if (ApplicationState.TryGetData(nameof(Cards), out IReadOnlyList<CardCopy>? cards)
            && ApplicationState.TryGetData(nameof(Seek), out SeekDto seek))
        {
            // persisted state should match set filters
            // TODO: find way to check filters are consistent

            return new SeekList<CardCopy>(
                cards, seek.HasPrevious, seek.HasNext, seek.IsPartial);
        }
        else
        {
            return await FetchDbCopiesAsync();
        }
    }

    private async Task<SeekList<CardCopy>> FetchDbCopiesAsync()
    {
        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        var filter = ParseTextFilter.Parse(Search);

        var cards = FilterCards(dbContext.Cards, filter, PickedColors)
            .Select(c => new CardCopy
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

        return await OrderCopies(cards, Order)

            .SeekBy((SeekDirection)Direction)
                .After(c => c.Id == Seek)
                .ThenTake(PageSize.Current)

            .ToSeekListAsync(_cancel.Token);
    }

    private static IQueryable<Card> FilterCards(IQueryable<Card> cards, TextFilter filter, Color color)
    {
        string? name = filter.Name?.ToUpperInvariant();
        string? text = filter.Text?.ToUpperInvariant();

        string[] types = filter.Types?.ToUpperInvariant().Split() ?? Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(name))
        {
            // keep eye on perf, postgres is slow here
            cards = cards
                .Where(c => c.Name.ToUpper().Contains(name));
        }

        if (filter.Mana is ManaFilter mana)
        {
            cards = cards.Where(mana.CreateFilter());
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            cards = cards
                .Where(c => c.Text != null
                    && c.Text.ToUpper().Contains(text));
        }

        foreach (string type in types)
        {
            cards = cards
                .Where(c => c.Type.ToUpper().Contains(type));
        }

        if (color is not Color.None)
        {
            cards = cards
                .Where(c => c.Color.HasFlag(color));
        }

        return cards;
    }

    private static IOrderedQueryable<CardCopy> OrderCopies(IQueryable<CardCopy> copies, string? orderBy)
    {
        return orderBy switch
        {
            nameof(Card.ManaCost) => copies
                .OrderByDescending(c => c.ManaValue)
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id),

            nameof(Card.SetName) => copies
                .OrderBy(c => c.SetName)
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.Id),

            nameof(Card.Rarity) => copies
                .OrderByDescending(c => c.Rarity)
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id),

            nameof(Card.Holds) => copies
                .OrderByDescending(c => c.Held) // keep eye on, query is a bit expensive
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id),

            nameof(Card.Name) or _ => copies
                .OrderBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id)
        };
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
