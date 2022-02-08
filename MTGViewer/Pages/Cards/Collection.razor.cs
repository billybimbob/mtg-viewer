using System;
using System.Collections.Generic;
using System.Collections.Paging;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Cards;


public partial class Collection : ComponentBase, IDisposable
{
    [Inject]
    protected IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected PageSizes PageSizes { get; set; } = default!;

    [Inject]
    protected ILogger<Collection> Logger { get; set; } = default!;


    public bool IsBusy => _isBusy;

    public FilterContext Filters => _filters;

    public OffsetList<Card> Cards => _loader.Cards ?? OffsetList<Card>.Empty();

    public int CardTotal(Card card) => _loader.CardTotal(card);


    private const int SearchNameLimit = 40;

    private bool _isBusy;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Loader _loader = new();
    private readonly FilterContext _filters = new();


    protected override async Task OnInitializedAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            _filters.PageSize = PageSizes.GetComponentSize<Collection>();

            await _loader.LoadCardsAsync(DbFactory, _filters, _cancel.Token);

            _filters.LoadContext = new LoadContext(this);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogError(ex.ToString());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex.ToString());
        }
        finally
        {
            _isBusy = false;
        }
    }


    public void Dispose()
    {
        _cancel.Cancel();
        _cancel.Dispose();
    }



    public sealed class FilterContext
    {
        private LoadContext _loadContext;
        internal LoadContext LoadContext
        {
            set => _loadContext = value;
        }

        private string? _searchName;
        public string? SearchName
        {
            get => _searchName;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = null;
                }

                if (_loadContext.IsBusy
                    || value?.Length > SearchNameLimit
                    || value == _searchName)
                {
                    return;
                }

                if (_pageIndex > 0)
                {
                    _pageIndex = 0;
                }

                _searchName = value;
                
                _loadContext.LoadCardsAsync();
            }
        }

        public bool IsReversed { get; private set; }

        private string _orderBy = nameof(Card.Name);
        public string OrderBy
        {
            get => _orderBy;
            private set
            {
                if (_loadContext.IsBusy)
                {
                    return;
                }

                if (value != _orderBy && _pageIndex > 0)
                {
                    _pageIndex = 0;
                }

                IsReversed = value == _orderBy && !IsReversed;
                _orderBy = value;

                _loadContext.LoadCardsAsync();
            }
        }

        public void Reorder<T>(Expression<Func<Card, T>> property)
        {
            if ((property.Body as MemberExpression)?.Member.Name
                is not string newOrder)
            {
                return;
            }

            OrderBy = newOrder;
        }

        private int _pageSize = 1;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (_loadContext.IsBusy
                    || value <= 0
                    || value == _pageSize)
                {
                    return;
                }

                if (_pageIndex > 0)
                {
                    _pageIndex = 0;
                }

                _pageSize = value;
                _loadContext.LoadCardsAsync();
            }
        }

        private int _pageIndex;
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (_loadContext.IsBusy
                    || value < 0
                    || value == _pageIndex
                    || value >= _loadContext.MaxPage)
                {
                    return;
                }

                _pageIndex = value;

                _loadContext.LoadCardsAsync();
            }
        }

        private readonly HashSet<string> _pickedColors = new(StringComparer.CurrentCultureIgnoreCase);
        public IReadOnlyCollection<string> PickedColors => _pickedColors;

        public void ToggleColor(string color)
        {
            if (_loadContext.IsBusy)
            {
                return;
            }

            if (_pickedColors.Contains(color))
            {
                _pickedColors.Remove(color);
            }
            else
            {
                _pickedColors.Add(color);
            }

            if (_pageIndex > 0)
            {
                _pageIndex = 0;
            }

            _loadContext.LoadCardsAsync();
        }
    }


    internal readonly struct LoadContext
    {
        private readonly Collection? _parent;

        public LoadContext(Collection parent)
        {
            _parent = parent;
        }

        public int MaxPage =>
            _parent?.Cards.Offset.Total ?? 0;

        public bool IsBusy => _parent?.IsBusy ?? false;

        public Task LoadCardsAsync()
        {
            return _parent is null
                ? Task.CompletedTask
                : _parent.LoadCardsAsync();
        }
    }


    private async Task LoadCardsAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await _loader.LoadCardsAsync(DbFactory, _filters, _cancel.Token);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogError(ex.ToString());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex.ToString());
        }
        finally
        {
            _isBusy = false;

            StateHasChanged();
        }
    }


    private sealed class Loader
    {
        private readonly HashSet<Card> _loadedCards = new();
        private readonly Dictionary<string, int> _cardAmounts = new();
        private OffsetList<Card>? _pagedCards;

        public OffsetList<Card>? Cards => _pagedCards;

        public int CardTotal(Card card)
        {
            if (card is null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            return _cardAmounts.GetValueOrDefault(card.Id);
        }


        public async Task LoadCardsAsync(
            IDbContextFactory<CardDbContext> dbFactory, 
            FilterContext filters,
            CancellationToken cancel)
        {
            await using var dbContext = await dbFactory.CreateDbContextAsync(cancel);

            dbContext.AttachRange(_loadedCards); // reuse prior card objs

            var newCards = await FilteredCardsAsync(dbContext, filters, cancel);
            var cardAmounts = await CardAmountsAsync(dbContext, newCards, cancel);

            _loadedCards.UnionWith(newCards);

            foreach ((string cardId, int total) in cardAmounts)
            {
                _cardAmounts[cardId] = total;
            }

            _pagedCards = newCards;
        }
    }



    #region Fetch Queries

    private static Task<OffsetList<Card>> FilteredCardsAsync(
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

        if (pickedColors.Any())
        {
            cards = cards
                .Where(c => c.Colors
                    .Any(cl => pickedColors.Contains(cl.Name)));
        }

        int pageSize = filters.PageSize;
        int pageIndex = filters.PageIndex;

        return CardsOrdered(cards, filters)
            .ToOffsetListAsync(pageIndex, pageSize, cancel);
    }


    private static IQueryable<Card> CardsOrdered(IQueryable<Card> cards, FilterContext filters)
    {
        bool isAscending = filters.OrderBy switch
        {
            nameof(Card.ManaCost) => true,
            nameof(Card.SetName) => true,
            nameof(Card.Rarity) => false,
            nameof(Card.Amounts) => false,
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
                PrimaryOrder(c => c.Cmc)
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id),

            nameof(Card.SetName) =>
                PrimaryOrder(c => c.SetName)
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.Id),

            nameof(Card.Amounts) => 
                PrimaryOrder(c => c.Amounts.Sum(a => a.NumCopies))
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


    private static Task<Dictionary<string, int>> CardAmountsAsync(
        CardDbContext dbContext, 
        IEnumerable<Card> cards,
        CancellationToken cancel)
    {
        var cardIds = cards
            .Select(c => c.Id)
            .ToArray();

        return dbContext.Amounts
            .Where(a => cardIds.Contains(a.CardId))
            .GroupBy(a => a.CardId,
                (CardId, amounts) =>
                    new { CardId, Total = amounts.Sum(a => a.NumCopies) })

            .ToDictionaryAsync(
                ct => ct.CardId, ct => ct.Total, cancel);
    }

    #endregion

}