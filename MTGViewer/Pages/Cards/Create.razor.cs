using System;
using System.Collections.Generic;
using System.Collections.Paging;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;
using MTGViewer.Services;

namespace MTGViewer.Pages.Cards;


public partial class Create
{
    public bool IsBusy => _isBusy;
    public bool HasNoNext => !_matchPage.HasNext;

    public IReadOnlyCollection<string> PickedColors => _search.PickedColors;
    public IReadOnlyCollection<string> PickedRarities => _search.PickedRarities;

    public string? MatchName
    {
        get => _search.MatchName;
        set => _search.MatchName = value;
    }

    public bool IsMultiColored
    {
        get => _search.IsMultiColored;
        set => _search.IsMultiColored = value;
    }

    public CardQuery Query => _search.Query;
    public IReadOnlyList<MatchInput> Matches => _matches;

    public SaveResult Result { get; set; }


    private const int SearchNameLimit = 40;

    private bool _isBusy;
    private readonly CancellationTokenSource _cancel = new();

    private readonly CardSearch _search = new();
    private EditContext? _searchEdit;
    private ValidationMessageStore? _resultErrors;

    private readonly List<MatchInput> _matches = new();
    private Offset _matchPage;


    protected override void OnInitialized()
    {
        _searchEdit = new(_search.Query);
        _resultErrors = new(_searchEdit);

        Reset();

        _searchEdit.OnValidationRequested += ClearErrors;
        _searchEdit.OnFieldChanged += ClearErrors;
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_searchEdit is not null)
            {
                _searchEdit.OnValidationRequested -= ClearErrors;
                _searchEdit.OnFieldChanged -= ClearErrors;
            }

            _cancel.Cancel();
            _cancel.Dispose();
        }

        base.Dispose(disposing);
    }



    public sealed class CardSearch
    {
        public CardQuery Query { get; } = new();

        public HashSet<string> PickedRarities { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> PickedColors { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsMultiColored { get; set; }

        private string? _matchName;
        public string? MatchName
        {
            get => _matchName;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = null;
                }

                if (value == _matchName || value?.Length > SearchNameLimit)
                {
                    return;
                }

                _matchName = value;
            }
        }
    }


    public sealed class MatchInput
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
    


    private void ClearErrors(object? sender, ValidationRequestedEventArgs args)
    {
        if (_searchEdit is null || _resultErrors is null)
        {
            return;
        }

        _resultErrors.Clear();
        _searchEdit.NotifyValidationStateChanged();
    }


    private void ClearErrors(object? sender, FieldChangedEventArgs args)
    {
        if (_searchEdit is null || _resultErrors is null)
        {
            return;
        }

        var idField = _searchEdit.Field(nameof(CardQuery.Id));

        _resultErrors.Clear(idField);
        _resultErrors.Clear(args.FieldIdentifier);

        _searchEdit.NotifyValidationStateChanged();
    }


    private void NoMatchError()
    {
        if (_searchEdit is null || _resultErrors is null)
        {
            return;
        }

        var idField = _searchEdit.Field(nameof(CardQuery.Id));
        var noMatch = new []{ "No matches were found" };

        _resultErrors.Add(idField, noMatch);
        _searchEdit.NotifyValidationStateChanged();
    }


    public void ColorToggle(string color)
    {
        if (_searchEdit is null)
        {
            return;
        }

        var pickedColors = _search.PickedColors;
        var colorField = _searchEdit.Field(nameof(CardQuery.Colors));

        if (pickedColors.Contains(color))
        {
            pickedColors.Remove(color);
        }
        else
        {
            pickedColors.Add(color);
        }

        _searchEdit.NotifyFieldChanged(colorField);
    }


    public void RarityToggle(string rarity)
    {
        var pickedRarities = _search.PickedRarities;

        if (pickedRarities.Contains(rarity))
        {
            pickedRarities.Remove(rarity);
        }
        else
        {
            pickedRarities.Add(rarity);
        }
    }


    public async Task SearchForCardAsync()
    {
        if (_isBusy || (_matchPage != default && !_matchPage.HasNext))
        {
            return;
        }

        Result = SaveResult.None;
        _isBusy = true;

        try
        {
            PrepareSearch(_search, _matchPage);

            var cancelToken = _cancel.Token;
            var limit = PageSizes.Limit;

            var fetch = ScopedServices.GetRequiredService<IMTGQuery>();

            var result = await fetch
                .Where(_search.Query)
                .SearchAsync(cancelToken);

            _matches.AddRange(
                result.Select(c => new MatchInput(c, limit)));

            _matchPage = result.Offset;

            if (!_matches.Any())
            {
                NoMatchError();
            }
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogError(ex.ToString());
        }
        finally
        {
            _isBusy = false;
        }
    }


    private static void PrepareSearch(CardSearch search, Offset matchPage)
    {
        var query = search.Query;
        var colorJoin = search.IsMultiColored ? IMTGQuery.And : IMTGQuery.Or;

        query.Colors = string.Join(colorJoin, search.PickedColors);
        query.Rarity = string.Join(IMTGQuery.Or, search.PickedRarities);

        query.Page = matchPage == default ? 0 : matchPage.Current + 1;
    }


    public bool MatchPassesFilters(MatchInput match)
    {
        const StringComparison ignoreCase = StringComparison.CurrentCultureIgnoreCase;

        string? matchName = _search.MatchName;
        var pickedColors = _search.PickedColors;

        var card = match.Card;
        var cardColors = card.Colors.Select(c => c.Name);

        bool nameMatches = string.IsNullOrWhiteSpace(matchName) 
            || card.Name.Contains(matchName, ignoreCase);

        bool colorMatches = !pickedColors.Any() 
            || pickedColors.Overlaps(cardColors);

        return nameMatches && colorMatches;
    }


    public void Reset()
    {
        if (_isBusy || _searchEdit is null)
        {
            return;
        }

        _matches.Clear();
        _matchPage = default;

        var query = _search.Query;

        _search.PickedColors.Clear();
        _search.PickedRarities.Clear();

        // TODO: use reflection to reset

        query.Name = default;
        query.Cmc = default;
        query.Colors = default;
        query.Rarity = default;
        query.SetName = default;

        query.Supertypes = default;
        query.Types = default;
        query.Subtypes = default;

        query.Artist = default;
        query.Power = default;
        query.Toughness = default;
        query.Loyalty = default;

        query.PageSize = PageSizes.GetComponentSize<Create>();

        // force data validation, might be inefficient
        _searchEdit.Validate();
    }


    public async Task AddNewCardsAsync()
    {
        if (_isBusy)
        {
            return;
        }

        var addedAmounts = _matches
            .Where(m => m.NumCopies > 0 && m.NumCopies <= PageSizes.Limit)
            .Select(m => new CardRequest(m.Card, m.NumCopies))
            .ToList();

        if (!addedAmounts.Any())
        {
            return;
        }

        Result = SaveResult.None;
        _isBusy = true;

        try
        {
            var cancelToken = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(cancelToken);

            await AddNewCardsAsync(dbContext, addedAmounts, PageSizes.Limit, cancelToken);

            await dbContext.AddCardsAsync(addedAmounts, cancelToken);

            await dbContext.SaveChangesAsync(cancelToken);

            Result = SaveResult.Success;
        }
        catch (DbUpdateException e)
        {
            Logger.LogError($"failed to add new cards {e}");

            Result = SaveResult.Error;
        }
        catch (InvalidOperationException e)
        {
            Logger.LogError($"failed to add new cards {e}");

            Result = SaveResult.Error;
        }
        catch (OperationCanceledException e)
        {
            Logger.LogError($"cancel error: {e}");

            Result = SaveResult.Error;
        }
        finally
        {
            _isBusy = false;

            Reset();
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


    private static Task<List<string>> ExistingCardIdsAsync(
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

            return dbContext.Cards
                .Select(c => c.Id)
                .AsAsyncEnumerable()
                .Intersect(cardIds)
                .ToListAsync(cancel)
                .AsTask();
        }
        else
        {
            var cardIds = cards
                .Select(c => c.Id)
                .ToArray();

            return dbContext.Cards
                .Select(c => c.Id)
                .Where(cid => cardIds.Contains(cid))
                .ToListAsync(cancel);
        }
    }
}