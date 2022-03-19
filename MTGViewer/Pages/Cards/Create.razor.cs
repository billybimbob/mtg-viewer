using System;
using System.Collections.Generic;
using System.Linq;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Data.Internal;
using MTGViewer.Services;

namespace MTGViewer.Pages.Cards;


[Authorize]
[Authorize(CardPolicies.ChangeTreasury)]
public partial class Create : OwningComponentBase
{
    [Parameter]
    [SupplyParameterFromQuery]
    public string? Name { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? MultiverseId { get; set; }

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
    public string[]? Types { get; set; }

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


    [Inject]
    protected IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected NavigationManager Nav { get; set; } = default!;

    [Inject]
    protected PageSizes PageSizes { get; set; } = default!;

    [Inject]
    protected ILogger<Create> Logger { get; set; } = default!;


    internal IReadOnlyList<MatchInput> Matches => _matches;

    internal bool HasNoNext => !_matchPage.HasNext;

    internal CardQuery Query { get; } = new();

    internal EditContext? SearchEdit { get; private set; }

    internal bool IsBusy { get; private set; }

    internal bool IsFromForm { get; private set; }

    internal SaveResult Result { get; set; }


    // should never change, only used for resets
    private static readonly CardQuery _empty = new();

    private readonly CancellationTokenSource _cancel = new();
    private readonly List<MatchInput> _matches = new();

    private ValidationMessageStore? _resultErrors;
    private Offset _matchPage;


    protected override void OnInitialized()
    {
        var edit = new EditContext(Query);
        _resultErrors = new(edit);

        edit.OnValidationRequested += ClearErrors;
        edit.OnFieldChanged += ClearErrors;

        SearchEdit = edit;
    }


    protected override async Task OnParametersSetAsync()
    {
        if (IsBusy)
        {
            return;
        }

        _matches.Clear();
        _matchPage = default;

        Result = SaveResult.None;
        IsBusy = true;

        try
        {
            if (!ValidateParameters())
            {
                NavigateToQuery(_empty);
                return;
            }

            if (Query == _empty)
            {
                IsFromForm = true;
                return;
            }

            await SearchForCardAsync(_cancel.Token);
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


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (SearchEdit is EditContext edit)
            {
                edit.OnValidationRequested -= ClearErrors;
                edit.OnFieldChanged -= ClearErrors;
            }

            _cancel.Cancel();
            _cancel.Dispose();
        }

        base.Dispose(disposing);
    }



    private void ClearErrors(object? sender, ValidationRequestedEventArgs args)
    {
        if (SearchEdit is not EditContext edit || _resultErrors is null)
        {
            return;
        }

        _resultErrors.Clear();
        edit.NotifyValidationStateChanged();
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
        var noMatch = new []{ "No matches were found" };

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


    private bool ValidateParameters()
    {
        if (SearchEdit is null)
        {
            return false;
        }

        var types = Types is { Length: > 0 }
            ? string.Join(' ', Types)
            : null;

        Query.Name = Name;
        Query.MultiverseId = MultiverseId;
        Query.Cmc = Cmc;
        Query.Colors = (Color)Colors;
        Query.Rarity = (Rarity?)Rarity;
        Query.SetName = Set;
        Query.Type = types;
        Query.Artist = Artist;
        Query.Power = Power;
        Query.Toughness = Toughness;
        Query.Loyalty = Loyalty;
        Query.Text = Text;
        Query.Flavor = Flavor;

        return SearchEdit.Validate();
    }


    private void NavigateToQuery(CardQuery query)
    {
        var color = query.Colors is not Color.None
            ? (int?)query.Colors
            : null;

        var parameters = new Dictionary<string, object?>
        {
            [nameof(Name)] = query.Name,
            [nameof(MultiverseId)] = query.MultiverseId,
            [nameof(Cmc)] = query.Cmc,
            [nameof(Colors)] = color,
            [nameof(Rarity)] = (int?)query.Rarity,
            [nameof(Set)] = query.SetName,
            [nameof(Types)] = query.Type?.Split(),
            [nameof(Artist)] = query.Artist,
            [nameof(Power)] = query.Power,
            [nameof(Loyalty)] = query.Loyalty,
            [nameof(Text)] = query.Text,
            [nameof(Flavor)] = query.Flavor
        };

        Nav.NavigateTo(
            Nav.GetUriWithQueryParameters(parameters), replace: true);
    }


    internal async Task ResetAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await Task.Yield();
            NavigateToQuery(_empty);
        }
        finally
        {
            IsBusy = false;
        }
    }


    internal async Task SubmitSearchAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await Task.Yield();
            NavigateToQuery(Query);
        }
        finally
        {
            IsBusy = false;
        }
    }


    internal sealed class MatchInput
    {
        private readonly int _limit;
        private int _numCopies;

        public MatchInput(Card card, int limit)
        {
            Card = card;

            _limit = limit;
        }

        public Card Card { get; }

        public int NumCopies
        {
            get => _numCopies;
            set
            {
                if (value >= 0 && value <= _limit)
                {
                    _numCopies = value;
                }
            }
        }
    }


    private async Task SearchForCardAsync(CancellationToken cancel)
    {
        var mtgQuery = ScopedServices.GetRequiredService<IMTGQuery>();

        var result = await ApplyQuery(mtgQuery).SearchAsync(cancel);

        int limit = PageSizes.Limit;
        var newMatches = result.Select(c => new MatchInput(c, limit));

        _matches.AddRange(newMatches);
        _matchPage = result.Offset;

        if (!_matches.Any())
        {
            NoMatchError();
        }
    }


    private IMTGCardSearch ApplyQuery(IMTGQuery search)
    {
        var types = Query.Type?.Split() ?? Enumerable.Empty<string>();

        int page = _matchPage == default ? 0 : _matchPage.Current + 1;

        return search
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
            .Where(c => c.Page == page) ;
    }


    internal async Task LoadMoreCardsAsync()
    {
        if (IsBusy || _matchPage is Offset { Total: >0, HasNext: false })
        {
            return;
        }

        IsBusy = true;

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
            IsBusy = false;
        }
    }


    internal async Task AddNewCardsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var addedCopies = _matches
            .Where(m => m.NumCopies > 0 && m.NumCopies <= PageSizes.Limit)
            .Select(m => new CardRequest(m.Card, m.NumCopies))
            .ToList();

        if (!addedCopies.Any())
        {
            return;
        }

        Result = SaveResult.None;
        IsBusy = true;

        try
        {
            var cancelToken = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(cancelToken);

            await AddNewCardsAsync(dbContext, addedCopies, PageSizes.Limit, cancelToken);

            await dbContext.AddCardsAsync(addedCopies, cancelToken);

            await dbContext.SaveChangesAsync(cancelToken);

            Result = SaveResult.Success;
        }
        catch (DbUpdateException e)
        {
            Logger.LogError("Failed to add new cards {Error}", e);

            Result = SaveResult.Error;
        }
        catch (OperationCanceledException e)
        {
            Logger.LogWarning("{Error}", e);

            Result = SaveResult.Error;
        }
        finally
        {
            IsBusy = false;

            await ResetAsync();
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


    private static async ValueTask<List<string>> ExistingCardIdsAsync(
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
            var cardIds = cards
                .Select(c => c.Id)
                .ToArray();

            return await dbContext.Cards
                .Select(c => c.Id)
                .Where(cid => cardIds.Contains(cid))
                .ToListAsync(cancel);
        }
    }
}