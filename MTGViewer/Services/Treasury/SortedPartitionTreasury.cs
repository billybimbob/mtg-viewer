using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;

namespace MTGViewer.Services;

public class SortedPartitionTreasury : ITreasuryQuery
{
    private readonly CardDbContext _dbContext;
    private readonly IDbContextFactory<CardDbContext> _dbFactory;
    private readonly ILogger<SortedPartitionTreasury> _logger;

    public SortedPartitionTreasury(
        CardDbContext dbContext, 
        IDbContextFactory<CardDbContext> dbFactory, 
        ILogger<SortedPartitionTreasury> logger)
    {
        _dbContext = dbContext;
        _dbFactory = dbFactory;
        _logger = logger;
    }


    public IQueryable<Box> Boxes => 
        _dbContext.Boxes
            .AsNoTrackingWithIdentityResolution();

    public IQueryable<Amount> Cards => 
        _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .AsNoTrackingWithIdentityResolution();


    #region Checkout

    public async Task<RequestResult> FindCheckoutAsync(
        IEnumerable<CardRequest> requests, 
        CancellationToken cancel = default)
    {
        requests = AsCheckoutRequests(requests);

        if (!requests.Any())
        {
            return RequestResult.Empty;
        }

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        var targets = await CheckoutTargetsAsync(dbContext, requests, cancel);

        return GetCheckouts(dbContext, targets, requests);
    }


    private static IReadOnlyList<CardRequest> AsCheckoutRequests(IEnumerable<CardRequest?>? requests)
    {
        if (requests is null)
        {
            throw new ArgumentNullException(nameof(requests));
        }

        var requestArray = requests.ToArray();

        if (requestArray.Any(cr => cr is null))
        {
            throw new ArgumentNullException(nameof(requests));
        }

        return requestArray
            .Cast<CardRequest>()
            .GroupBy(
                req => req.Card.Id,
                (_, requests) => requests.First() with
                { 
                    NumCopies = requests.Sum(req => req.NumCopies) 
                })
            .ToList();
    }


    private static Task<List<Amount>> CheckoutTargetsAsync(
        CardDbContext dbContext,
        IEnumerable<CardRequest> requests, 
        CancellationToken cancel)
    {
        var requestIds = requests
            .Select(cr => cr.Card.Id)
            .ToArray();

        var requestCards = requests
            .Select(cr => cr.Card);

        dbContext.Cards.AttachRange(requestCards);

        // track requests for modifications
        // also to keep original Card since it should not be modified

        return dbContext.Amounts
            .Where(ca => ca.Location is Box
                && ca.NumCopies > 0
                && requestIds.Contains(ca.CardId))

            .Include(ca => ca.Card)
            .Include(ca => ca.Location)

            .OrderBy(ca => ca.Id)
            .ToListAsync(cancel);
    }


    private static RequestResult GetCheckouts(
        CardDbContext dbContext,
        IReadOnlyList<Amount> targets,
        IEnumerable<CardRequest> checkouts)
    {
        if (!targets.Any())
        {
            return RequestResult.Empty;
        }

        ApplyExactCheckouts(targets, checkouts);
        ApplyApproxCheckouts(targets, checkouts);

        var updated = ModifiedAmounts(dbContext).ToList();

        return AsResult(dbContext, updated);
    }


    private static void ApplyExactCheckouts(
        IEnumerable<Amount> targets, IEnumerable<CardRequest> checkouts)
    {
        var exactMatches = checkouts
            .GroupJoin( targets,
                req => req.Card.Id,
                tar => tar.CardId,
                (request, matches) => (request, matches));

        foreach (var (request, matches) in exactMatches)
        {
            ApplyTargetRequests(request, matches);
        }
    }


    private static void ApplyApproxCheckouts(
        IEnumerable<Amount> targets, IEnumerable<CardRequest> checkouts)
    {
        var checkoutsByName = checkouts
            .Where(req => req.NumCopies > 0)
            .GroupBy(req => req.Card.Name,
                (_, requests) => requests.First() with
                {
                    // just truncate since Card name is only 
                    // thing that matters
                    // keep eye on
                    NumCopies = requests.Sum(req => req.NumCopies)
                });

        var targetsByName = targets
            .Where(ca => ca.NumCopies > 0)
            .GroupBy(ca => ca.Card.Name,
                (_, amounts) => new CardNameGroup(amounts));

        var approxMatches = checkoutsByName 
            .Join( targetsByName,
                check => check.Card.Name,
                matches => matches.Name,
                (check, matches) => (check, matches));

        foreach (var (request, matches) in approxMatches)
        {
            ApplyTargetRequests(request, matches);
        }
    }


    private static void ApplyTargetRequests(
        CardRequest request, IEnumerable<Amount> targets)
    {
        using var targetIter = targets.GetEnumerator();

        while (request.NumCopies > 0 && targetIter.MoveNext())
        {
            var target = targetIter.Current;
            int amountTaken = Math.Min(request.NumCopies, target.NumCopies);

            if (amountTaken == 0)
            {
                continue;
            }

            target.NumCopies -= amountTaken;
            request.NumCopies -= amountTaken;
        }
    }

    #endregion


    private static IEnumerable<Amount> ModifiedAmounts(CardDbContext dbContext) =>
        dbContext.ChangeTracker
            .Entries<Amount>()
            .Where(e => e.State is EntityState.Modified)
            .Select(e => e.Entity)
            .Where(a => a.Location is Box);


    private static RequestResult AsResult(CardDbContext dbContext, IReadOnlyList<Amount> amounts)
    {
        if (!amounts.Any())
        {
            return RequestResult.Empty;
        }

        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

        var originalCopies = dbContext.ChangeTracker
            .Entries<Amount>()
            .IntersectBy(amounts, e => e.Entity)
            .Select(e => 
                (e.Entity.Id, e.Property(a => a.NumCopies).OriginalValue))
            .ToDictionary(
                io => io.Id, io => io.OriginalValue);

        return new RequestResult(amounts, originalCopies);
    }


    #region Return

    public async Task<RequestResult> FindReturnAsync(
        IEnumerable<CardRequest> requests, 
        CancellationToken cancel = default)
    {
        requests = AsReturnRequests(requests); 

        if (!requests.Any())
        {
            return RequestResult.Empty;
        }

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        var sortedBoxes = await SortedBoxesAsync(dbContext, requests, cancel); 

        return GetReturns(dbContext, sortedBoxes, requests);
    }


    private static IReadOnlyList<CardRequest> AsReturnRequests(IEnumerable<CardRequest?>? requests)
    {
        if (requests is null)
        {
            throw new ArgumentNullException(nameof(requests));
        }

        requests = requests.ToArray();

        if (requests.Any(req => req is null))
        {
            throw new ArgumentNullException(nameof(requests));
        }

        // descending so that the first added cards do not shift down the 
        // positioning of the sorted card amounts
        // each of the returned cards should have less effect on following returns
        // keep eye on

        return requests
            .Cast<CardRequest>()
            .GroupBy(
                req => req.Card.Id,
                (_, requests) => requests.First() with
                {
                    NumCopies = requests.Sum(req => req.NumCopies)
                })
            .OrderByDescending(req => req.Card.Name)
                .ThenByDescending(req => req.Card.SetName)
            .ToList();
    }


    private Task<List<Box>> SortedBoxesAsync(
        CardDbContext dbContext,
        IEnumerable<CardRequest> requests,
        CancellationToken cancel)
    {
        var requestCards = requests
            .Select(cr => cr.Card);

        dbContext.AttachRange(requestCards);

        // loading all shared cards, could be memory inefficient
        // TODO: find more efficient way to determining card position
        // unbounded: keep eye on

        return dbContext.Boxes
            .Include(b => b.Cards)
                .ThenInclude(ca => ca.Card)
            .OrderBy(s => s.Id)
            .ToListAsync(cancel);
    }


    private static RequestResult GetReturns(
        CardDbContext dbContext,
        IReadOnlyList<Box> sortedBoxes,
        IEnumerable<CardRequest> returns)
    {
        if (!sortedBoxes.Any())
        {
            return RequestResult.Empty;
        }

        var state = new ReturnState(sortedBoxes);

        ApplyExistingReturns(state, returns);
        ApplyNewReturns(state, returns);

        var allReturns = ModifiedAmounts(dbContext)
            .Concat(state.AddedAmounts)
            .ToList();

        return AsResult(dbContext, allReturns);
    }


    private sealed class ReturnState
    {
        private readonly Dictionary<int, int> _boxSpace;
        private readonly Dictionary<QuantityIndex, Amount> _amountMap;

        public ReturnState(IReadOnlyList<Box> boxes)
        {
            SortedBoxes = boxes;
            SortedAmounts = GetSortedAmounts(boxes);

            _boxSpace = boxes.ToDictionary(
                b => b.Id,
                b => b.Cards.Sum(a => a.NumCopies));

            _amountMap = SortedAmounts.ToDictionary(
                amt => (QuantityIndex)amt);
        }

        public IReadOnlyList<Box> SortedBoxes { get; }

        public IReadOnlyList<Amount> SortedAmounts { get; }

        public IReadOnlyDictionary<int, int> BoxSpace => _boxSpace;

        public IEnumerable<Amount> AddedAmounts =>
            _amountMap.Values.Except( SortedAmounts );


        private static List<Amount> GetSortedAmounts(IEnumerable<Box> boxes) =>
            boxes
                .SelectMany(b => b.Cards)
                .OrderBy(ca => ca.Card.Name)
                    .ThenBy(ca => ca.Card.SetName)
                    .ThenByDescending(a => (a.Location as Box)!.Capacity)
                .ToList();


        public void Deconstruct(
            out IReadOnlyList<Box> boxes,
            out IReadOnlyList<Amount> amounts,
            out IReadOnlyDictionary<int, int> boxSpace)
        {
            boxes = SortedBoxes;
            amounts = SortedAmounts;
            boxSpace = BoxSpace;
        }


        public void Update(Card card, Box box, int addedCopies)
        {
            int boxId = box.Id;
            var index = new QuantityIndex(card.Id, boxId);

            if (!_amountMap.ContainsKey(index))
            {
                throw new ArgumentException($"{nameof(card)} and {nameof(box)}");
            }

            if (!_boxSpace.ContainsKey(boxId))
            {
                throw new ArgumentException(nameof(box));
            }

            _amountMap[index].NumCopies += addedCopies;
            _boxSpace[boxId] += addedCopies;
        }


        public void Add(Card card, Box box, int numCopies)
        {
            string cardId = card.Id;
            int boxId = box.Id;
            var index = new QuantityIndex(cardId, boxId);

            if (_amountMap.ContainsKey(index))
            {
                throw new ArgumentException($"{nameof(card)} and {nameof(box)}");
            }

            if (!_boxSpace.ContainsKey(boxId))
            {
                throw new ArgumentException(nameof(box));
            }

            // avoid tracking the added amount so that the
            // primary key will not be set
            // there is also no prop fixup, so all props fully specified

            _amountMap[index] = new Amount
            {
                CardId = cardId,
                Card = card,
                LocationId = boxId,
                Location = box,
                NumCopies = numCopies
            };

            _boxSpace[boxId] += numCopies;
        }
    }


    private static void ApplyExistingReturns(
        ReturnState state, IEnumerable<CardRequest> requests)
    {
        var (_, sortedAmounts, boxSpace) = state;

        var existingSpots = sortedAmounts
            // each group should be ordered by box capacity
            .ToLookup(ca => ca.CardId, ca => (Box)ca.Location);

        foreach (CardRequest request in requests)
        {
            (Card card, int numCopies) = request;
            var possibleBoxes = existingSpots[card.Id];

            if (!possibleBoxes.Any())
            {
                continue;
            }

            var splitToBoxes = FitToBoxes(possibleBoxes, boxSpace, numCopies);

            foreach ((Box box, int splitCopies) in splitToBoxes)
            {
                state.Update(card, box, splitCopies);
                // changes will cascade to the split iter via boxSpace

                request.NumCopies -= splitCopies;
            }
        }
    }


    private static IEnumerable<(Box, int)> FitToBoxes(
        IEnumerable<Box> boxes,
        IReadOnlyDictionary<int, int> boxSpace,
        int cardsToAssign)
    {
        foreach (var box in boxes)
        {
            int spaceUsed = boxSpace.GetValueOrDefault(box.Id);
            int remainingSpace = Math.Max(0, box.Capacity - spaceUsed);

            var newCopies = Math.Min(cardsToAssign, remainingSpace);

            if (newCopies == 0)
            {
                continue;
            }

            yield return (box, newCopies);

            cardsToAssign -= newCopies;

            if (cardsToAssign == 0)
            {
                yield break;
            }
        }
    }


    private static void ApplyNewReturns(
        ReturnState returnState, IEnumerable<CardRequest> returns)
    {
        if (returns.All(cr => cr.NumCopies == 0))
        {
            return;
        }

        var boxSearch = new BoxSearcher(returnState);
        var boxSpace = returnState.BoxSpace;

        foreach ((Card card, int numCopies) in returns)
        {
            if (numCopies == 0)
            {
                continue;
            }

            var boxOptions = boxSearch.FindBestBoxes(card);
            var splitToBoxes = FitAllToBoxes(boxOptions, boxSpace, numCopies);

            foreach ((Box box, int splitCopies) in splitToBoxes)
            {
                // returnState changes will cascade to the split iter via boxSpace
                returnState.Add(card, box, splitCopies);
            }
        }
    }


    private sealed class BoxSearcher
    {
        private readonly IReadOnlyList<Box> _sortedBoxes;

        private readonly List<Card> _sortedCards;
        private readonly CardNameComparer _cardComparer;

        private readonly List<int> _addPositions;
        private readonly List<int> _boxBoundaries;


        public BoxSearcher(ReturnState returnState)
        {
            var (sortedBoxes, sortedAmounts, _) = returnState;

            _sortedBoxes = sortedBoxes;

            _sortedCards = sortedAmounts
                .Select(ca => ca.Card)
                .ToList();

            _cardComparer = new CardNameComparer();

            _addPositions = GetAddPositions(sortedAmounts).ToList();
            _boxBoundaries = GetBoxBoundaries(sortedBoxes).ToList();
        }

        public IEnumerable<Box> FindBestBoxes(Card card)
        {
            int cardIndex = _sortedCards.BinarySearch(card, _cardComparer);

            if (cardIndex < 0)
            {
                cardIndex = ~cardIndex;
            }

            int addPosition = _addPositions.ElementAtOrDefault(cardIndex);
            int boxIndex = _boxBoundaries.BinarySearch(addPosition);

            if (boxIndex < 0)
            {
                boxIndex = Math.Min(~boxIndex, _sortedBoxes.Count - 1);
            }

            return _sortedBoxes.Skip(boxIndex);
        }


        private static IEnumerable<int> GetAddPositions(IEnumerable<Amount> boxAmounts)
        {
            int amountSum = 0;

            foreach (Amount amount in boxAmounts)
            {
                yield return amountSum;

                amountSum += amount.NumCopies;
            }
        }


        private static IEnumerable<int> GetBoxBoundaries(IEnumerable<Box> boxes)
        {
            int capacitySum = 0;

            foreach (Box box in boxes)
            {
                capacitySum += box.Capacity;

                yield return capacitySum;
            }
        }
    }


    private static IEnumerable<(Box, int)> FitAllToBoxes(
        IEnumerable<Box> boxes,
        IReadOnlyDictionary<int, int> boxSpace,
        int cardsToAssign)
    {
        var fitInBoxes = FitToBoxes(boxes.SkipLast(1), boxSpace, cardsToAssign);

        foreach (var fit in fitInBoxes)
        {
            yield return fit;

            (_, int newCopies) = fit;

            cardsToAssign -= newCopies;

            if (cardsToAssign == 0)
            {
                yield break;
            }
        }

        var last = boxes.Last();

        yield return (last, cardsToAssign);
    }

    #endregion
}
