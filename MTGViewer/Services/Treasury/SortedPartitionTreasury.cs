using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;

namespace MTGViewer.Services;

public class SortedPartitionTreasury : ITreasuryQuery, IDisposable
{
    private readonly CardDbContext _dbContext;
    private readonly SemaphoreSlim _lock; // needed since CardDbContext is not thread safe
    private readonly ILogger<SortedPartitionTreasury> _logger;

    public SortedPartitionTreasury(CardDbContext dbContext, ILogger<SortedPartitionTreasury> logger)
    {
        _dbContext = dbContext;
        _lock = new(1, 1);
        _logger = logger;
    }


    public void Dispose()
    {
        _lock.Dispose();
    }


    public IQueryable<Box> Boxes => 
        _dbContext.Boxes
            .AsNoTrackingWithIdentityResolution();

    public IQueryable<Amount> Cards => 
        _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .AsNoTrackingWithIdentityResolution();



    #region Checkout

    public async Task<IReadOnlyList<Amount>> FindCheckoutAsync(
        IEnumerable<CardRequest> requests, 
        CancellationToken cancel = default)
    {
        requests = CheckedRequests(requests);

        if (!requests.Any())
        {
            return Array.Empty<Amount>();
        }

        await _lock.WaitAsync(cancel);

        try
        {
            var targets = await CheckoutTargetsAsync(requests, cancel);

            return GetCheckouts(targets, requests);
        }
        finally
        {
            _lock.Release();
        }
    }


    private static IEnumerable<CardRequest> CheckedRequests(IEnumerable<CardRequest?>? requests)
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


    private Task<List<Amount>> CheckoutTargetsAsync(
        IEnumerable<CardRequest> requests, CancellationToken cancel)
    {
        var requestIds = requests
            .Select(cr => cr.Card.Id)
            .Distinct()
            .ToArray();

        // track requests for modifications

        return _dbContext.Amounts
            .Where(ca => ca.Location is Box
                && ca.NumCopies > 0
                && requestIds.Contains(ca.CardId))

            .Include(ca => ca.Card)
            .Include(ca => ca.Location)

            .OrderBy(ca => ca.Id)
            .ToListAsync(cancel);
    }


    private IReadOnlyList<Amount> GetCheckouts(
        IReadOnlyList<Amount> targets, IEnumerable<CardRequest> checkouts)
    {
        if (!targets.Any())
        {
            return Array.Empty<Amount>();
        }

        ApplyExactCheckouts(targets, checkouts);
        ApplyApproxCheckouts(targets, checkouts);

        return GetUpdatedAmounts();
    }


    private static IReadOnlyList<CardRequest> ApplyExactCheckouts(
        IEnumerable<Amount> targets,
        IEnumerable<CardRequest> checkouts)
    {
        var unfinished = new List<CardRequest>();

        var exactMatches = checkouts
            .GroupJoin( targets,
                req => req.Card.Id,
                tar => tar.CardId,
                (request, matches) => (request, matches));

        foreach ((CardRequest request, IEnumerable<Amount> matches) in exactMatches)
        {
            ApplyTargetRequests(request, matches);
        }

        return unfinished;
    }


    private static void ApplyApproxCheckouts(
        IEnumerable<Amount> targets,
        IEnumerable<CardRequest> checkouts)
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

        foreach ((CardRequest request, CardNameGroup matches) in approxMatches)
        {
            ApplyTargetRequests(request, matches);
        }
    }


    private static void ApplyTargetRequests(CardRequest request, IEnumerable<Amount> targets)
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


    public IReadOnlyList<Amount> GetUpdatedAmounts()
    {
        var tracker = _dbContext.ChangeTracker;

        var amounts = tracker.Entries<Amount>()
            .Where(e => e.State != EntityState.Unchanged)
            .Select(e => e.Entity)
            .ToList();

        tracker.Clear();

        return amounts;
    }


    #region Return

    public async Task<IReadOnlyList<Amount>> FindReturnAsync(
        IEnumerable<CardRequest> requests, 
        CancellationToken cancel = default)
    {
        requests = MergedRequests(requests); 

        if (!requests.Any())
        {
            return Array.Empty<Amount>();
        }

        await _lock.WaitAsync(cancel);

        try
        {
            var sortedBoxes = await SortedBoxesAsync(cancel); 

            return GetReturns(sortedBoxes, requests);
        }
        finally
        {
            _lock.Release();
        }
    }


    private static IReadOnlyList<CardRequest> MergedRequests(IEnumerable<CardRequest?>? requests)
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


    private Task<List<Box>> SortedBoxesAsync(CancellationToken cancel) =>
        // loading all shared cards, could be memory inefficient
        // TODO: find more efficient way to determining card position
        // unbounded: keep eye on

        _dbContext.Boxes
            .Include(b => b.Cards)
                .ThenInclude(ca => ca.Card)
            .OrderBy(s => s.Id)
            .ToListAsync(cancel);


    private IReadOnlyList<Amount> GetReturns(
        IReadOnlyList<Box> sortedBoxes,
        IEnumerable<CardRequest> returns)
    {
        if (!sortedBoxes.Any())
        {
            return Array.Empty<Amount>();
        }

        var state = new ReturnState(sortedBoxes);

        ApplyExistingReturns(state, returns);
        ApplyNewReturns(state, returns);

        return GetUpdatedAmounts();
    }


    private class ReturnState
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


        public Amount GetAmount(string cardId, int boxId) => 
            _amountMap[(cardId, boxId)];

        public void AddToBox(int boxId, int numCopies) =>
            _boxSpace[boxId] += numCopies;
    }


    private static void ApplyExistingReturns(
        ReturnState state,
        IEnumerable<CardRequest> requests)
    {
        var (_, sortedAmounts, boxSpace) = state;

        var existingSpots = sortedAmounts
            // each group should be ordered by box capacity
            .ToLookup(ca => ca.CardId, ca => (Box)ca.Location);

        foreach (CardRequest request in requests)
        {
            string cardId = request.Card.Id;

            if (!existingSpots.Contains(cardId))
            {
                continue;
            }

            var possibleBoxes = existingSpots[cardId];
            var splitToBoxes = FitToBoxes(possibleBoxes, boxSpace, request.NumCopies);

            foreach ((int boxId, int splitCopies) in splitToBoxes)
            {
                var amount = state.GetAmount(cardId, boxId);

                // changes will cascade to the split iter via boxSpace
                state.AddToBox(boxId, splitCopies);

                amount.NumCopies += splitCopies;
                request.NumCopies -= splitCopies;
            }
        }
    }


    private static IEnumerable<(int BoxId, int NumCopies)> FitToBoxes(
        IEnumerable<Box> boxes,
        IReadOnlyDictionary<int, int> boxSpace,
        int cardsToAssign)
    {
        foreach (var box in boxes)
        {
            int spaceUsed = boxSpace.GetValueOrDefault(box.Id);
            int remainingSpace = Math.Max(0, box.Capacity - spaceUsed);

            var newNumCopies = Math.Min(cardsToAssign, remainingSpace);

            if (newNumCopies == 0)
            {
                continue;
            }

            yield return (box.Id, newNumCopies);

            cardsToAssign -= newNumCopies;

            if (cardsToAssign == 0)
            {
                yield break;
            }
        }
    }


    private void ApplyNewReturns(
        ReturnState returnState,
        IEnumerable<CardRequest> returns)
    {
        var boxSearch = new BoxSearcher(returnState);
        var boxSpace = returnState.BoxSpace;

        foreach ((Card returning, int numCopies) in returns)
        {
            if (numCopies == 0)
            {
                continue;
            }

            string cardId = returning.Id;

            var boxOptions = boxSearch.FindBestBoxes(returning);
            var splitToBoxes = FitAllToBoxes(boxOptions, boxSpace, numCopies);

            foreach ((int boxId, int splitCopies) in splitToBoxes)
            {
                // returnState changes will cascade to the split iter via boxSpace
                returnState.AddToBox(boxId, splitCopies);

                var newAmount = new Amount
                {
                    CardId = cardId,
                    LocationId = boxId,
                    NumCopies = splitCopies
                };

                _dbContext.Amounts.Attach(newAmount);
            }
        }
    }


    private class BoxSearcher
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


    private static IEnumerable<(int BoxId, int NumCopies)> FitAllToBoxes(
        IEnumerable<Box> boxes,
        IReadOnlyDictionary<int, int> boxSpace,
        int cardsToAssign)
    {
        var fitInBoxes = FitToBoxes(boxes.SkipLast(1), boxSpace, cardsToAssign);

        foreach (var fit in fitInBoxes)
        {
            cardsToAssign -= fit.NumCopies;

            yield return fit;

            if (cardsToAssign == 0)
            {
                yield break;
            }
        }

        var last = boxes.Last();

        yield return (last.Id, cardsToAssign);
    }


    #endregion
}
