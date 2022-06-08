using System;
using System.Collections.Generic;
using System.Linq;
using EntityFrameworkCore.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

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

    internal bool HasNoNext => !_matchPage.HasNext;

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal bool IsEmpty => Query == Empty;

    internal CardQuery Query { get; } = new();

    internal EditContext? SearchEdit { get; private set; }

    internal bool IsFromForm { get; private set; }

    internal SaveResult Result { get; set; }

    // should never change, only used for resets
    private static readonly CardQuery Empty = new();

    private readonly CancellationTokenSource _cancel = new();
    private readonly List<MatchInput> _matches = new();

    private bool _isBusy;
    private bool _isInteractive;

    private PersistingComponentStateSubscription _persistSubscription;
    private ValidationMessageStore? _resultErrors;
    private Offset _matchPage;
    private string? _returnUrl;

    protected override void OnInitialized()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistMatches);

        var edit = new EditContext(Query);

        _resultErrors = new ValidationMessageStore(edit);

        edit.OnFieldChanged += ClearErrors;

        SearchEdit = edit;
    }

    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            _matches.Clear();
            _matchPage = default;

            if (!UpdateQuery())
            {
                Logger.LogWarning("Given search parameters were invalid");

                NavigateToQuery(Empty);
                return;
            }

            if (ReturnUrl is not null)
            {
                // ensure no open redirects
                _returnUrl = ReturnUrl.StartsWith(Nav.BaseUri)
                    ? ReturnUrl
                    : $"{Nav.BaseUri}{ReturnUrl.TrimStart('/')}";
            }

            if (Query == Empty)
            {
                IsFromForm = true;
                return;
            }

            await LoadCardMatchesAsync(_cancel.Token);
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

        if (SearchEdit is EditContext edit)
        {
            edit.OnFieldChanged -= ClearErrors;
        }

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

        ApplicationState.PersistAsJson(nameof(_matchPage), _matchPage);

        return Task.CompletedTask;
    }

    private async Task LoadCardMatchesAsync(CancellationToken cancel)
    {
        if (_matches.Any()
            || !ApplicationState.TryGetData(nameof(_matches), out IEnumerable<Card>? cards)
            || !ApplicationState.TryGetData(nameof(MatchInput.HasDetails), out HashSet<string>? inDbCards)
            || !ApplicationState.TryGetData(nameof(_matchPage), out Offset offset))
        {
            await SearchForCardAsync(cancel);
            return;
        }

        int limit = PageSize.Limit;

        var matches = cards
            .Select(c => new MatchInput(c, inDbCards.Contains(c.Id), limit));

        _matches.AddRange(matches);
        _matchPage = offset;
    }

    private void ClearErrors(object? sender, FieldChangedEventArgs args)
    {
        if (SearchEdit is not EditContext edit || _resultErrors is null)
        {
            return;
        }

        var idField = edit.Field(nameof(CardQuery.Id));

        _resultErrors.Clear(idField);
        _resultErrors.Clear(args.FieldIdentifier);

        edit.NotifyValidationStateChanged();
    }

    private void NoMatchError()
    {
        if (SearchEdit is not EditContext edit || _resultErrors is null)
        {
            return;
        }

        var idField = edit.Field(nameof(CardQuery.Id));

        string[] noMatch = { "No matches were found" };

        _resultErrors.Add(idField, noMatch);
        edit.NotifyValidationStateChanged();
    }

    internal void ToggleColor(Color toggle)
    {
        if (SearchEdit is not EditContext edit)
        {
            return;
        }

        var colorField = edit.Field(nameof(CardQuery.Colors));

        if (Query.Colors.HasFlag(toggle))
        {
            Query.Colors &= ~toggle;
        }
        else
        {
            Query.Colors |= toggle;
        }

        edit.NotifyFieldChanged(colorField);
    }

    private bool UpdateQuery()
    {
        if (SearchEdit is null)
        {
            return false;
        }

        Query.Name = Name;
        Query.Cmc = Cmc < 0 ? null : Cmc;
        Query.Colors = ValidatedColor(Colors);
        Query.Rarity = ValidatedRarity(Rarity);
        Query.SetName = Set;
        Query.Type = Types;
        Query.Artist = Artist;
        Query.Power = Power;
        Query.Toughness = Toughness;
        Query.Loyalty = Loyalty;
        Query.Text = Text;
        Query.Flavor = Flavor;

        return SearchEdit.Validate();
    }

    private static Color ValidatedColor(int value)
    {
        var color = (Color)value;

        return Enum
            .GetValues<Color>()
            .Select(c => c & color)
            .Aggregate((x, y) => x | y);
    }

    private static Rarity? ValidatedRarity(int? value)
    {
        var rarity = (Rarity?)value;

        return Enum
            .GetValues<Rarity>()
            .OfType<Rarity?>()
            .FirstOrDefault(r => r == rarity);
    }

    private void NavigateToQuery(CardQuery query)
    {
        var parameters = new Dictionary<string, object?>
        {
            [nameof(Name)] = query.Name,
            [nameof(Cmc)] = query.Cmc,
            [nameof(Colors)] = query.Colors is Color.None ? null : (int)query.Colors,

            [nameof(Rarity)] = (int?)query.Rarity,
            [nameof(Set)] = query.SetName,
            [nameof(Types)] = query.Type,

            [nameof(Artist)] = query.Artist,
            [nameof(Power)] = query.Power,
            [nameof(Loyalty)] = query.Loyalty,
            [nameof(Text)] = query.Text,
            [nameof(Flavor)] = query.Flavor
        };

        Nav.NavigateTo(
            Nav.GetUriWithQueryParameters(parameters), replace: true);
    }

    internal void Reset()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        Result = SaveResult.None;

        NavigateToQuery(Empty);
    }

    internal void SubmitSearch()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        Result = SaveResult.None;

        NavigateToQuery(Query);
    }

    internal sealed class MatchInput
    {
        public MatchInput(Card card, bool hasDetails, int limit)
        {
            Card = card;
            HasDetails = hasDetails;
            Limit = limit;
        }

        public Card Card { get; }

        public bool HasDetails { get; }

        public int Limit { get; }

        private int _copies;
        public int Copies
        {
            get => _copies;
            set
            {
                if (value >= 0 && value <= Limit)
                {
                    _copies = value;
                }
            }
        }
    }

    private async Task SearchForCardAsync(CancellationToken cancel)
    {
        var result = await SearchQueryAsync(cancel);

        _matchPage = result.Offset;

        await AddNewMatchesAsync(result, cancel);

        if (!_matches.Any())
        {
            NoMatchError();
        }
    }

    private async Task<OffsetList<Card>> SearchQueryAsync(CancellationToken cancel)
    {
        var types = Query.Type?.Split() ?? Enumerable.Empty<string>();

        int page = _matchPage == default ? 0 : _matchPage.Current + 1;

        return await MtgQuery
            .Where(c => c.Name == Query.Name)
            .Where(c => c.SetName == Query.SetName)

            .Where(c => c.Cmc == Query.Cmc)
            .Where(c => c.Colors == Query.Colors)

            .Where(c => types.All(t => c.Type == t))
            .Where(c => c.Rarity == Query.Rarity)

            .Where(c => c.Power == Query.Power)
            .Where(c => c.Toughness == Query.Toughness)
            .Where(c => c.Loyalty == Query.Loyalty)

            .Where(c => c.Text!.Contains(Query.Text ?? string.Empty))
            .Where(c => c.Flavor!.Contains(Query.Flavor ?? string.Empty))
            .Where(c => c.Artist == Query.Artist)
            .Where(c => c.Page == page)

            .SearchAsync(cancel);
    }

    private async Task AddNewMatchesAsync(IEnumerable<Card> cards, CancellationToken cancel)
    {
        if (!cards.Any())
        {
            return;
        }

        string[] matchIds = cards
            .Select(c => c.Id)
            .ToArray();

        await using var dbContext = await DbFactory.CreateDbContextAsync(cancel);

        var existingIds = await dbContext.Cards
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .Where(cid => matchIds.Contains(cid))
            .AsAsyncEnumerable()
            .ToHashSetAsync(cancel);

        int limit = PageSize.Limit;

        var newMatches = cards
            .Select(c => new MatchInput(c, existingIds.Contains(c.Id), limit));

        _matches.AddRange(newMatches);
    }

    internal async Task LoadMoreCardsAsync()
    {
        if (_isBusy || _matchPage is { Total: > 0, HasNext: false })
        {
            return;
        }

        _isBusy = true;

        try
        {
            await SearchForCardAsync(_cancel.Token);
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

    internal async Task AddNewCardsAsync()
    {
        if (_isBusy)
        {
            return;
        }

        var addedCopies = _matches
            .Where(m => m.Copies > 0 && m.Copies <= PageSize.Limit)
            .Select(m => new CardRequest(m.Card, m.Copies))
            .ToList();

        if (!addedCopies.Any())
        {
            return;
        }

        _isBusy = true;

        try
        {
            Result = SaveResult.None;

            var token = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            await AddNewCardsAsync(dbContext, addedCopies, PageSize.Limit, token);

            await dbContext.AddCardsAsync(addedCopies, token);

            await dbContext.SaveChangesAsync(token);

            Result = SaveResult.Success;

            if (_returnUrl is not null)
            {
                Nav.NavigateTo(_returnUrl, forceLoad: true);
            }
            else
            {
                NavigateToQuery(Empty);
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
