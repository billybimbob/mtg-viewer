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
using MtgViewer.Data.Access;
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
    internal ICardRepository CardRepository { get; set; } = default!;

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
    internal bool HasNext => _currentPage < _totalResults;
    internal bool CanAddCards => _matches.Any(m => m.Copies > 0);

    internal bool IsSearchError { get; private set; }
    internal SaveResult Result { get; set; }

    private readonly CancellationTokenSource _cancel = new();
    private readonly List<MatchInput> _matches = new();

    private bool _isBusy;
    private bool _isInteractive;

    private PersistingComponentStateSubscription _persistSubscription;

    private int _currentPage;
    private int _totalResults;
    private string? _returnUrl;

    protected override void OnInitialized()
        => _persistSubscription = ApplicationState.RegisterOnPersisting(PersistMatches);

    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            if (ReturnUrl is not null)
            {
                // ensure no open redirects
                _returnUrl = ReturnUrl.StartsWith(Nav.BaseUri)
                    ? ReturnUrl
                    : $"{Nav.BaseUri}{ReturnUrl.TrimStart('/')}";
            }

            if (!TryLoadStateData())
            {
                await SearchForCardAsync(0);
            }
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

    private Task PersistMatches()
    {
        var cards = _matches.Select(m => m.Card);

        var inDbCards = _matches
            .Where(m => m.HasDetails)
            .Select(m => m.Card.Id)
            .ToHashSet();

        ApplicationState.PersistAsJson(nameof(_matches), cards);
        ApplicationState.PersistAsJson(nameof(MatchInput.HasDetails), inDbCards);
        ApplicationState.PersistAsJson(nameof(_totalResults), _totalResults);

        return Task.CompletedTask;
    }

    private bool TryLoadStateData()
    {
        if (ApplicationState.TryGetData(nameof(_matches), out IEnumerable<Card>? cards)
            && ApplicationState.TryGetData(nameof(MatchInput.HasDetails), out ICollection<string>? inDbCards)
            && ApplicationState.TryGetData(nameof(_totalResults), out int totalResults))
        {
            var matches = cards
                .Select(c => new MatchInput(c, inDbCards.Contains(c.Id), PageSize.Limit));

            _matches.Clear();
            _matches.AddRange(matches);
            _totalResults = totalResults;

            if (_matches.Count == 0)
            {
                Result = SaveResult.Error;
                IsSearchError = true;
            }

            return true;
        }

        return false;
    }

    private async Task SearchForCardAsync(int page)
    {
        var search = new CardSearch
        {
            Name = Name,
            ManaValue = Cmc,
            Colors = (Color)Colors,
            Rarity = (Rarity?)Rarity,
            SetName = Set,
            Types = Types,
            Artist = Artist,
            Power = Power,
            Toughness = Toughness,
            Loyalty = Loyalty,
            Text = Text,
            Flavor = Flavor,
        };

        if (search.IsEmpty)
        {
            Result = SaveResult.Error;
            IsSearchError = true;
            return;
        }

        search.Page = _currentPage = page;
        search.PageSize = PageSize.Current;

        var result = await MtgQuery.SearchAsync(search, _cancel.Token);

        _totalResults = result.Offset.Total;

        if (result.Count > 0)
        {
            var existingIds = await CardRepository.GetExistingCardIdsAsync(result, _cancel.Token);

            var newMatches = result
                .Select(c => new MatchInput(c, existingIds.Contains(c.Id), PageSize.Limit));

            _matches.AddRange(newMatches);
        }

        if (_matches.Count == 0)
        {
            Result = SaveResult.Error;
            IsSearchError = true;
        }
    }

    internal async Task LoadMoreCardsAsync()
    {
        if (_isBusy || !HasNext)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await SearchForCardAsync(_currentPage + 1);
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
        if (!_isBusy)
        {
            Nav.NavigateTo("/Cards/Search");
        }
    }

    internal async Task AddNewCardsAsync()
    {
        if (_isBusy || !CanAddCards)
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
            await CardRepository.AddCardsAsync(addedCopies, _cancel.Token);

            Result = SaveResult.Success;

            if (_returnUrl is not null)
            {
                Nav.NavigateTo(_returnUrl, forceLoad: true);
            }
            else
            {
                Nav.NavigateTo("/Cards/Search");
            }
        }
        catch (DbUpdateException e)
        {
            Logger.LogError("Failed to add new cards {Error}", e);

            Result = SaveResult.Error;
            IsSearchError = false;

            _isBusy = false;
        }
        catch (OperationCanceledException e)
        {
            Logger.LogWarning("{Error}", e);

            Result = SaveResult.Error;
            IsSearchError = false;

            _isBusy = false;
        }
    }
}
