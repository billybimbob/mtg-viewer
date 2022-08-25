using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Services;
using MtgViewer.Services.Search;
using MtgViewer.Utils;

namespace MtgViewer.Pages.Cards;

[Authorize]
[Authorize(CardPolicies.ChangeTreasury)]
public sealed partial class Create : ComponentBase, IDisposable
{
    [Parameter]
    [SupplyParameterFromQuery]
    public string? Name { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public int? Cmc { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public int Colors { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public int? Rarity { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? Set { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? Types { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? Artist { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? Power { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? Toughness { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? Loyalty { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? Text { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? Flavor { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? ReturnUrl { get; set; }

    [Inject]
    internal IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    internal IMtgQuery MtgQuery { get; set; } = default!;

    [Inject]
    internal NavigationManager Nav { get; set; } = default!;

    [Inject]
    internal PersistentComponentState ApplicationState { get; set; } = default!;

    [Inject]
    internal PageSize PageSize { get; set; } = default!;

    [Inject]
    internal ILogger<Create> Logger { get; set; } = default!;

    internal IReadOnlyList<MatchInput> Matches => _matches;

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal bool HasNoNext => Search.Page + 1 >= _totalResults;

    internal CardSearch Search { get; } = new();

    internal bool IsFromForm { get; private set; }

    internal bool IsSearchError { get; private set; }

    internal SaveResult Result { get; set; }

    private readonly CancellationTokenSource _cancel = new();
    private readonly List<MatchInput> _matches = new();

    private bool _isBusy;
    private bool _isInteractive;

    private PersistingComponentStateSubscription _persistSubscription;

    private int _totalResults;
    private string? _returnUrl;

    protected override void OnInitialized()
        => _persistSubscription = ApplicationState.RegisterOnPersisting(PersistMatches);

    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            UpdateSearch();

            if (ReturnUrl is not null)
            {
                // ensure no open redirects

                _returnUrl = ReturnUrl.StartsWith(Nav.BaseUri)
                    ? ReturnUrl
                    : $"{Nav.BaseUri}{ReturnUrl.TrimStart('/')}";
            }

            if (Search.IsEmpty)
            {
                IsFromForm = true;
                return;
            }

            await LoadCardMatchesAsync();
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Warning}", ex);
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

    private void UpdateSearch()
    {
        _matches.Clear();

        Search.Name = Name;
        Search.ManaValue = Cmc;
        Search.Colors = (Color)Colors;
        Search.Rarity = (Rarity?)Rarity;
        Search.SetName = Set;
        Search.Types = Types;
        Search.Artist = Artist;
        Search.Power = Power;
        Search.Toughness = Toughness;
        Search.Loyalty = Loyalty;
        Search.Text = Text;
        Search.Flavor = Flavor;
        Search.Page = 0;
        Search.PageSize = 0;

        _totalResults = 0;
    }

    private Task PersistMatches()
    {
        const string details = nameof(MatchInput.HasDetails);

        var cards = _matches.Select(m => m.Card);

        var inDbCards = _matches
            .Where(m => m.HasDetails)
            .Select(m => m.Card.Id)
            .ToHashSet();

        ApplicationState.PersistAsJson(nameof(_totalResults), _totalResults);
        ApplicationState.PersistAsJson(nameof(_matches), cards);
        ApplicationState.PersistAsJson(details, inDbCards);

        return Task.CompletedTask;
    }

    private async Task LoadCardMatchesAsync()
    {
        const string details = nameof(MatchInput.HasDetails);

        if (_matches.Count > 0
            || !ApplicationState.TryGetData(nameof(_totalResults), out _totalResults)
            || !ApplicationState.TryGetData(nameof(_matches), out IEnumerable<Card>? cards)
            || !ApplicationState.TryGetData(details, out ICollection<string>? inDbCards))
        {
            await SearchForCardAsync();
            return;
        }

        var matches = cards
            .Select(c => new MatchInput(c, inDbCards.Contains(c.Id), PageSize.Limit));

        _matches.AddRange(matches);
    }

    private async Task SearchForCardAsync()
    {
        if (Search.PageSize == 0)
        {
            Search.PageSize = PageSize.Current;
        }

        var result = await MtgQuery.SearchAsync(Search, _cancel.Token);

        _totalResults = result.Offset.Total;

        await AddNewMatchesAsync(result);

        if (_matches.Count == 0)
        {
            Result = SaveResult.Error;
            IsSearchError = true;
        }
    }

    private async Task AddNewMatchesAsync(IReadOnlyList<Card> cards)
    {
        if (cards.Count == 0)
        {
            return;
        }

        string[] matchIds = cards
            .Select(c => c.Id)
            .ToArray();

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        var existingIds = await dbContext.Cards
            .Where(c => matchIds.Contains(c.Id))
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .AsAsyncEnumerable()
            .ToHashSetAsync(_cancel.Token);

        var newMatches = cards
            .Select(c => new MatchInput(c, existingIds.Contains(c.Id), PageSize.Limit));

        _matches.AddRange(newMatches);
    }

    internal async Task LoadMoreCardsAsync()
    {
        if (_isBusy || HasNoNext)
        {
            return;
        }

        _isBusy = true;

        try
        {
            Search.Page += 1;

            await SearchForCardAsync();
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

    internal void Reset()
    {
        if (_isBusy || Search.IsEmpty)
        {
            return;
        }

        _isBusy = true;

        Result = SaveResult.None;

        NavigateToSearch(CardSearch.Empty);
    }

    internal void SubmitSearch()
    {
        if (_isBusy || Search.IsEmpty)
        {
            return;
        }

        _isBusy = true;

        Result = SaveResult.None;

        NavigateToSearch(Search);
    }

    private void NavigateToSearch(IMtgSearch search)
    {
        var parameters = new Dictionary<string, object?>
        {
            [nameof(Name)] = search.Name,
            [nameof(Cmc)] = search.ManaValue,
            [nameof(Colors)] = search.Colors is Color.None ? null : (int)search.Colors,

            [nameof(Rarity)] = (int?)search.Rarity,
            [nameof(Set)] = search.SetName,
            [nameof(Types)] = search.Types,

            [nameof(Power)] = search.Power,
            [nameof(Loyalty)] = search.Loyalty,

            [nameof(Artist)] = search.Artist,
            [nameof(Text)] = search.Text,
            [nameof(Flavor)] = search.Flavor
        };

        Nav.NavigateTo(
            Nav.GetUriWithQueryParameters(parameters), replace: true);
    }

    internal async Task AddNewCardsAsync()
    {
        if (_isBusy || _matches.All(m => m.Copies == 0))
        {
            return;
        }

        var addedCopies = _matches
            .Where(m => m.Copies > 0 && m.Copies <= PageSize.Limit)
            .Select(m => new CardRequest(m.Card, m.Copies))
            .ToList();

        if (addedCopies.Count == 0)
        {
            return;
        }

        _isBusy = true;

        Result = SaveResult.None;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await AddNewCardsAsync(dbContext, addedCopies, PageSize.Limit, _cancel.Token);

            await dbContext.AddCardsAsync(addedCopies, _cancel.Token);

            await dbContext.SaveChangesAsync(_cancel.Token);

            Result = SaveResult.Success;

            if (_returnUrl is not null)
            {
                Nav.NavigateTo(_returnUrl, forceLoad: true);
            }
            else
            {
                NavigateToSearch(CardSearch.Empty);
            }
        }
        catch (DbUpdateException e)
        {
            Logger.LogError("Failed to add new cards {Error}", e);

            Result = SaveResult.Error;

            _isBusy = false;
        }
        catch (OperationCanceledException e)
        {
            Logger.LogWarning("{Error}", e);

            Result = SaveResult.Error;

            _isBusy = false;
        }
    }

    private static async Task AddNewCardsAsync(
        CardDbContext dbContext,
        IReadOnlyList<CardRequest> requests,
        int limit,
        CancellationToken cancel)
    {
        var requestCards = requests
            .Select(cr => cr.Card)
            .ToList();

        var existingIds = await ExistingCardIdsAsync(dbContext, requestCards, limit, cancel);

        var existingCards = requestCards
            .IntersectBy(existingIds, c => c.Id);

        var newCards = requestCards
            .ExceptBy(existingIds, c => c.Id);

        dbContext.Cards.AttachRange(existingCards);
        dbContext.Cards.AddRange(newCards);
    }

    private static async Task<IReadOnlyList<string>> ExistingCardIdsAsync(
        CardDbContext dbContext,
        IReadOnlyList<Card> cards,
        int limit,
        CancellationToken cancel)
    {
        if (cards.Count > limit)
        {
            var cardIds = cards
                .Select(c => c.Id)
                .ToAsyncEnumerable();

            return await dbContext.Cards
                .Select(c => c.Id)
                .AsAsyncEnumerable()
                .Intersect(cardIds)
                .ToListAsync(cancel);
        }
        else
        {
            string[] cardIds = cards
                .Select(c => c.Id)
                .ToArray();

            return await dbContext.Cards
                .Select(c => c.Id)
                .Where(cid => cardIds.Contains(cid))
                .ToListAsync(cancel);
        }
    }
}
