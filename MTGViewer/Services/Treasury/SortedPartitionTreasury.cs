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

    public async Task<IReadOnlyList<Withdrawl>> FindCheckoutAsync(
        IEnumerable<CardRequest> requests, 
        IEnumerable<Alteration>? extraChanges = null,
        CancellationToken cancel = default)
    {
        requests = CheckedRequests(requests);

        if (!requests.Any())
        {
            return Array.Empty<Withdrawl>();
        }

        await _lock.WaitAsync(cancel);

        try
        {
            var targets = await CheckoutTargetsAsync(requests, extraChanges, cancel);

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

        var requestSet = requests.ToHashSet();

        if (requestSet.Any(cr => cr is null))
        {
            throw new ArgumentNullException(nameof(requests));
        }

        requestSet.RemoveWhere(req => req!.NumCopies == 0);

        return requestSet!;
    }


    private async Task<IReadOnlyCollection<Amount>> CheckoutTargetsAsync(
        IEnumerable<CardRequest> requests,
        IEnumerable<Alteration>? extraChanges,
        CancellationToken cancel)
    {
        var requestIds = requests
            .Select(cr => cr.Card.Id)
            .Distinct()
            .ToArray();

        var targets = await _dbContext.Amounts
            .Where(ca => ca.Location is Box
                && ca.NumCopies > 0
                && requestIds.Contains(ca.CardId))

            .Include(ca => ca.Card)
            .Include(ca => ca.Location)

            .AsNoTrackingWithIdentityResolution()
            .ToDictionaryAsync(amt => amt.Id, cancel);

        if (extraChanges is not null)
        {
            ApplyExtraChanges(targets, extraChanges);
        }

        return targets.Values;
    }


    private static void ApplyExtraChanges(
        IDictionary<int, Amount> targets,
        IEnumerable<Alteration> extraChanges)
    {
        foreach (Alteration alter in extraChanges)
        {
            Amount? amount;
            switch (alter)
            {
                case Withdrawl(int amountId, int numCopies):

                    if (!targets.TryGetValue(amountId, out amount))
                    {
                        throw new ArgumentException(nameof(extraChanges));
                    }
                    else
                    {
                        amount.NumCopies -= numCopies;
                    }
                    break;

                case Addition(int amountId, int numCopies):

                    if (!targets.TryGetValue(amountId, out amount))
                    {
                        throw new ArgumentException(nameof(extraChanges));
                    }
                    else
                    {
                        amount.NumCopies += numCopies;
                    }
                    break;

                case Extension:
                default:
                    break;
            }
        }
    }


    private static IReadOnlyList<Withdrawl> GetCheckouts(
        IReadOnlyCollection<Amount> targets,
        IEnumerable<CardRequest> checkouts)
    {
        if (!targets.Any())
        {
            return Array.Empty<Withdrawl>();
        }

        var validTargets = targets
            .Where(ca => ca.NumCopies > 0);

        var foundExact = GetExactCheckouts(validTargets, checkouts)
            .ToList();

        var unfinished = GetUnfinished(foundExact, checkouts);
        var foundApprox = GetApproxCheckouts(validTargets, unfinished);

        return foundExact
            .Concat(foundApprox)
            .Select(found => (Withdrawl)found)
            .ToList();
    }


    private static IEnumerable<AmountRequest> GetExactCheckouts(
        IEnumerable<Amount> targets,
        IEnumerable<CardRequest> checkouts)
    {
        var exactMatches = checkouts
            .GroupJoin(targets,
                req => req.Card.Id,
                tar => tar.CardId,
                (request, matches) => (matches, request.NumCopies));

        return GetResultsFromMatches(exactMatches);
    }


    private static IEnumerable<AmountRequest> GetApproxCheckouts(
        IEnumerable<Amount> targets,
        IEnumerable<CardRequest> checkouts)
    {
        var targetsByName = targets
            .GroupBy(ca => ca.Card.Name,
                (name, amounts) => (name, amounts));

        var checkoutsByName = checkouts
            .GroupBy(req => req.Card.Name,
                (name, requests) => 
                    (name, numCopies: requests.Sum(req => req.NumCopies)));

        var approxMatches = targetsByName
            .Join( checkoutsByName,
                group => group.name,
                checks => checks.name,
                (matches, checks) => (matches.amounts, checks.numCopies));

        return GetResultsFromMatches(approxMatches);
    }


    private static IEnumerable<AmountRequest> GetResultsFromMatches(
        IEnumerable<(IEnumerable<Amount>, int)> matches)
    {
        foreach ((IEnumerable<Amount> targets, int requestCopies) in matches)
        {
            using var targetIter = targets.GetEnumerator();
            int remaining = requestCopies;

            while (remaining > 0 && targetIter.MoveNext())
            {
                var target = targetIter.Current;
                int amountTaken = Math.Min(requestCopies, target.NumCopies);

                if (amountTaken == 0)
                {
                    continue;
                }

                target.NumCopies -= amountTaken;
                remaining -= amountTaken;

                yield return new AmountRequest(target, amountTaken);
            }
        }
    }

    #endregion



    private record AmountRequest(Amount Amount, int NumCopies)
    {
        public static explicit operator Withdrawl(AmountRequest result)
        {
            (Amount amount, int numCopies) = result;

            return new Withdrawl(amount.Id, numCopies);
        }

        public static explicit operator Addition(AmountRequest result)
        {
            (Amount amount, int numCopies) = result;

            return new Addition(amount.Id, numCopies);
        }
    }


    private static IEnumerable<CardRequest> GetUnfinished(
        IReadOnlyList<AmountRequest> currentResults,
        IEnumerable<CardRequest> allRequests)
    {
        // existing will always be a subset of the requested amounts

        return allRequests
            .GroupJoin( currentResults,
                req => req.Card.Id,
                res => res.Amount.CardId,
                (request, results) => 
                {
                    int resultCopies = results.Sum(res => res.NumCopies);
                    int remaining = request.NumCopies - resultCopies;

                    return (request, remaining);
                })
            .Where(rr => rr.remaining > 0)
            .Select(rr => rr.request with { NumCopies = rr.remaining });
    }



    #region Return

    public async Task<IReadOnlyList<Deposit>> FindReturnAsync(
        IEnumerable<CardRequest> requests, 
        IEnumerable<Alteration>? extraChanges = null,
        CancellationToken cancel = default)
    {
        requests = MergedRequests(requests); 

        if (!requests.Any())
        {
            return Array.Empty<Deposit>();
        }

        await _lock.WaitAsync(cancel);

        try
        {
            var sortedBoxes = await SortedBoxesAsync(cancel); 

            return GetReturns(sortedBoxes, extraChanges, requests);
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

        // descending so that the first added cards do not shift down the 
        // positioning of the sorted card amounts
        // each of the returned cards should have less effect on following returns
        // keep eye on

        var merged = requests
            .GroupBy(cr => cr?.Card,
                (card, crs) => card is null
                    ? null 
                    : new CardRequest(card, crs.Sum(cr => cr!.NumCopies)))
            .OrderBy(cr => cr?.Card.Name)
                .ThenBy(cr => cr?.Card.SetName)

            .ToList();

        if (merged.Any(req => req is null))
        {
            throw new ArgumentNullException(nameof(requests));
        }

        return merged!;
    }


    private Task<List<Box>> SortedBoxesAsync(CancellationToken cancel) =>
        // loading all shared cards, could be memory inefficient
        // TODO: find more efficient way to determining card position
        // unbounded: keep eye on

        _dbContext.Boxes
            .OrderBy(s => s.Id)
            .Include(b => b.Cards)
                .ThenInclude(ca => ca.Card)

            .AsNoTrackingWithIdentityResolution()
            .ToListAsync(cancel);


    private static IReadOnlyList<Deposit> GetReturns(
        IReadOnlyList<Box> sortedBoxes,
        IEnumerable<Alteration>? extraChanges,
        IEnumerable<CardRequest> returns)
    {
        if (!sortedBoxes.Any())
        {
            return Array.Empty<Deposit>();
        }

        var state = new ReturnState(sortedBoxes, extraChanges);

        var foundReturns = GetExistingReturns(state, returns)
            .ToList();

        var unfinished = GetUnfinished(foundReturns, returns);

        var addition = foundReturns
            .Select(found => (Addition)found);

        var extension = GetNewReturns(state, unfinished);

        return Enumerable.Empty<Deposit>()
            .Concat(addition)
            .Concat(extension)
            .ToList();
    }


    private class ReturnState
    {
        private readonly Dictionary<int, int> _boxSpace;
        private readonly Dictionary<QuantityIndex, Amount> _amountMap;

        public ReturnState(
            IReadOnlyList<Box> boxes,
            IEnumerable<Alteration>? extraChanges)
        {
            SortedBoxes = boxes;
            SortedAmounts = GetSortedAmounts(boxes);

            _boxSpace = boxes.ToDictionary(
                b => b.Id,
                b => b.Cards.Sum(a => a.NumCopies));

            _amountMap = SortedAmounts.ToDictionary(
                amt => (QuantityIndex)amt);

            if (extraChanges is not null)
            {
                ApplyExtraChanges(extraChanges);
            }
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


        private void ApplyExtraChanges(IEnumerable<Alteration> extraChanges)
        {
            var amountsById = SortedAmounts.ToDictionary(amt => amt.Id);

            foreach (Alteration alter in extraChanges)
            {
                Amount? amount;
                int locationId;

                switch (alter)
                {
                    case Withdrawl(int amountId, int numCopies):
                        amount = GetAmount(amountId);
                        locationId = amount.LocationId;

                        CheckValidBox(locationId);

                        _boxSpace[locationId] -= numCopies;
                        amount.NumCopies -= numCopies;
                        break;

                    case Addition(int amountId, int numCopies):
                        amount = GetAmount(amountId);
                        locationId = amount.LocationId;

                        CheckValidBox(locationId);

                        _boxSpace[locationId] += numCopies;
                        amount.NumCopies += numCopies;
                        break;

                    case Extension(_, int boxId, int numCopies):

                        CheckValidBox(boxId);
                        _boxSpace[boxId] += numCopies;
                        break;

                    default:
                        break;
                }
            }

            Amount GetAmount(int amountId)
            {
                return amountsById.GetValueOrDefault(amountId)
                    ?? throw new ArgumentException(nameof(extraChanges));
            }

            void CheckValidBox(int boxId)
            {
                if (!_boxSpace.ContainsKey(boxId))
                {
                    throw new ArgumentException(nameof(extraChanges));
                }
            }
        }


        public bool TryGetAmount(string cardId, int boxId, out Amount amount)
        {
            (string, int) amtKey = (cardId, boxId);
            bool success = _amountMap.TryGetValue(amtKey, out Amount? result);

            amount = result ?? null!;

            return success;
        }


        public void ApplyReturn(string cardId, int boxId, int numCopies)
        {
            var amtKey = (cardId, boxId);

            if (_amountMap.TryGetValue(amtKey, out var amount))
            {
                amount.NumCopies += numCopies;
            }

            _boxSpace[boxId] += numCopies;
        }


        public void Deconstruct(
            out IReadOnlyList<Box> boxes,
            out IReadOnlyList<Amount> amounts,
            out IReadOnlyDictionary<int, int> boxSpace)
        {
            boxes = SortedBoxes;
            amounts = SortedAmounts;
            boxSpace = BoxSpace;
        }
    }


    private static IEnumerable<AmountRequest> GetExistingReturns(
        ReturnState state,
        IEnumerable<CardRequest> requests)
    {
        var (_, sortedAmounts, boxSpace) = state;

        var existingSpots = sortedAmounts
            // each group should be ordered by box capacity
            .ToLookup(ca => ca.CardId, ca => (Box)ca.Location);

        foreach ((Card card, int numCopies) in requests)
        {
            string cardId = card.Id;

            if (!existingSpots.Contains(cardId))
            {
                continue;
            }

            var possibleBoxes = existingSpots[cardId];
            var splitToBoxes = FitToBoxes(possibleBoxes, boxSpace, numCopies);

            foreach ((int boxId, int splitCopies) in splitToBoxes)
            {
                if (!state.TryGetAmount(cardId, boxId, out var amount))
                {
                    continue;
                }

                // returnState changes will cascade to the split iter via boxSpace
                state.ApplyReturn(cardId, boxId, splitCopies);

                yield return new AmountRequest(amount, splitCopies);
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


    private static IEnumerable<Extension> GetNewReturns(
        ReturnState returnState,
        IEnumerable<CardRequest> returns)
    {
        var addState = new AddState(returnState);
        var boxSpace = returnState.BoxSpace;

        foreach ((Card returning, int numCopies) in returns)
        {
            string cardId = returning.Id;

            var boxOptions = addState.FindBoxesToAddCard(returning);
            var splitToBoxes = FitAllToBoxes(boxOptions, boxSpace, numCopies);

            foreach ((int boxId, int splitCopies) in splitToBoxes)
            {
                // returnState changes will cascade to the split iter via boxSpace
                returnState.ApplyReturn(cardId, boxId, splitCopies);

                yield return new Extension(cardId, boxId, splitCopies);
            }
        }
    }


    private class AddState
    {
        private readonly IReadOnlyList<Box> _sortedBoxes;

        private readonly List<Card> _sortedCards;
        private readonly CardNameComparer _cardComparer;

        private readonly List<int> _addPositions;
        private readonly List<int> _boxBoundaries;


        public AddState(ReturnState returnState)
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

        public IEnumerable<Box> FindBoxesToAddCard(Card card)
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