using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MTGViewer.Data;

#nullable enable

namespace MTGViewer.Services
{
    public sealed class FlatVariableStorage : ITreasury, IDisposable
    {
        private readonly CardDbContext _dbContext;
        private readonly SemaphoreSlim _lock; // needed since CardDbContext is not thread safe
        private readonly ILogger<FlatVariableStorage> _logger;

        public FlatVariableStorage(CardDbContext dbContext, ILogger<FlatVariableStorage> logger)
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


        private IQueryable<Box> SortedBoxes =>
            _dbContext.Boxes.OrderBy(s => s.Id);

        private IQueryable<Amount> SortedAmounts =>
            // loading all shared cards, could be memory inefficient
            // TODO: find more efficient way to determining card position

            _dbContext.Amounts
                .Where(ca => ca.Location is Box)
                .Include(ca => ca.Card)
                .OrderBy(ca => ca.Card.Name)
                    .ThenBy(ca => ca.Card.SetName); 



        public async Task<Transaction> ReturnAsync(
            IEnumerable<CardReturn> returns, CancellationToken cancel = default)
        {
            if (InvalidReturns(returns))
            {
                throw new ArgumentException("Given returns are invalid");
            }

            await _lock.WaitAsync(cancel);

            try
            {
                cancel.ThrowIfCancellationRequested();

                var boxSpace = await GetBoxSpaceAsync(cancel);

                cancel.ThrowIfCancellationRequested();

                var newTransaction = new Transaction();
                var mergedReturns = MergedReturns(returns).ToList();

                await ReturnExistingAsync(newTransaction, boxSpace, mergedReturns, cancel);

                cancel.ThrowIfCancellationRequested();

                if (mergedReturns.Any())
                {
                    await ReturnNewAsync(newTransaction, boxSpace, mergedReturns, cancel);

                    cancel.ThrowIfCancellationRequested();
                }

                _dbContext.Transactions.Attach(newTransaction);

                // intentionally leave db exception unhandled
                await _dbContext.SaveChangesAsync(cancel);

                cancel.ThrowIfCancellationRequested();

                return newTransaction;
            }
            finally
            {
                _lock.Release();
            }
        }


        private bool InvalidReturns(IEnumerable<CardReturn> returns)
        {
            return !returns.Any() 
                || returns.Any(cr => cr.Card == null || cr.NumCopies <= 0);
        }


        private Task<Dictionary<int, int>> GetBoxSpaceAsync(CancellationToken cancel)
        {
            return _dbContext.Boxes
                .Select(b => new { b.Id, Space = b.Cards.Sum(ca => ca.NumCopies) })
                .ToDictionaryAsync(
                    ba => ba.Id, ba => ba.Space, cancel);
        }


        private IEnumerable<CardReturn> MergedReturns(IEnumerable<CardReturn> returns)
        {
            return returns
                .GroupBy( cr => (cr.Card, cr.Deck),
                    (cd, crs) =>
                        new CardReturn(cd.Card, crs.Sum(cr => cr.NumCopies), cd.Deck))

                // descending so that the first added cards do not shift down the 
                // positioning of the sorted card amounts
                // each of the returned cards should have less effect on following returns
                // TODO: keep eye on

                .OrderByDescending(cr => cr.Card.Name)
                    .ThenByDescending(cr => cr.Card.SetName);
        }


        private async Task ReturnExistingAsync(
            Transaction transaction,
            Dictionary<int, int> boxSpace,
            List<CardReturn> returning,
            CancellationToken cancel)
        {
            var returnAmounts = await ExistingAmounts(returning)
                .ToListAsync(cancel); // unbounded: keep eye on

            cancel.ThrowIfCancellationRequested();

            if (!returnAmounts.Any())
            {
                return;
            }

            var allReturns = returning.ToArray();

            var returnBoxes = returnAmounts
                .ToLookup(ca => ca.CardId, ca => (Box)ca.Location);

            returning.Clear();

            foreach (var cardReturn in allReturns)
            {
                var (card, numCopies, deck) = cardReturn;

                if (!returnBoxes.Contains(card.Id))
                {
                    returning.Add(cardReturn);
                    continue;
                }

                var boxOptions = returnBoxes[card.Id];

                var splitBoxAmounts = FitToBoxes(boxOptions, boxSpace, numCopies);

                var targets = returnAmounts
                    .Join( splitBoxAmounts,
                        amt => (amt.CardId, amt.LocationId),
                        boxAmt => (card.Id, boxAmt.Box.Id),
                        (amt, boxAmt) => (amt, boxAmt.Amount));

                int totalReturn = ApplyReturnMatches(deck, targets, boxSpace, transaction.Changes);

                int notReturned = numCopies - totalReturn;

                if (notReturned != 0)
                {
                    returning.Add(cardReturn with { NumCopies = notReturned });
                }
            }
        }


        private IQueryable<Amount> ExistingAmounts(IEnumerable<CardReturn> returns)
        {
            var returnIds = returns
                .Select(cr => cr.Card.Id)
                .Distinct()
                .ToArray();

            return _dbContext.Amounts
                .Where(ca => ca.Location is Box && returnIds.Contains(ca.CardId))
                .Include(ca => ca.Card)
                .Include(ca => ca.Location)
                .OrderBy(ca => (ca.Location as Box)!.Capacity);
        }


        private int ApplyReturnMatches(
            Deck? source,
            IEnumerable<(Amount, int)> matches,
            IDictionary<int, int> boxSpace,
            IList<Change> changes)
        {
            int totalReturn = 0;

            foreach (var (target, splitAmount) in matches)
            {
                var newChange = new Change
                {
                    Card = target.Card,
                    To = target.Location,
                    From = source,
                    Amount = splitAmount
                };

                target.NumCopies += splitAmount;
                boxSpace[target.LocationId] += splitAmount;

                changes.Add(newChange);

                totalReturn += splitAmount;
            }
            
            return totalReturn;
        }


        private async Task ReturnNewAsync(
            Transaction transaction,
            Dictionary<int, int> boxSpace,
            IReadOnlyList<CardReturn> newReturns,
            CancellationToken cancel)
        {
            var sortedBoxes = await SortedBoxes.ToListAsync(cancel); // unbounded: keep eye on

            cancel.ThrowIfCancellationRequested();

            if (!sortedBoxes.Any())
            {
                throw new InvalidOperationException(
                    "There are no possible boxes to return the cards to");
            }

            var sortedAmounts = await SortedAmounts.ToListAsync(cancel); // unbounded: keep eye on

            cancel.ThrowIfCancellationRequested();

            var returnPairs = FindNewReturnPairs(newReturns, sortedAmounts, sortedBoxes, boxSpace);

            foreach (var (card, numCopies, source, box) in returnPairs)
            {
                var newSpot = new Amount
                {
                    Card = card,
                    Location = box,
                    NumCopies = numCopies
                };

                _dbContext.Amounts.Attach(newSpot);

                var newChange = new Change
                {
                    Card = card,
                    To = box,
                    From = source,
                    Amount = numCopies,
                    Transaction = transaction
                };

                transaction.Changes.Add(newChange);
                boxSpace[box.Id] += numCopies;
            }
        }


        private IEnumerable<(Card, int, Deck?, Box)> FindNewReturnPairs(
            IReadOnlyList<CardReturn> newReturns,
            IReadOnlyList<Amount> sortedAmounts,
            IReadOnlyList<Box> sortedBoxes,
            IReadOnlyDictionary<int, int> boxSpace)
        {
            var cardComparer = new CardNameComparer();

            var sortedCards = sortedAmounts
                .Select(ca => ca.Card)
                .ToList();

            var positions = GetAddPositions(sortedAmounts).ToList();
            var boundaries = GetBoxBoundaries(sortedBoxes).ToList();
            
            foreach (var (returnCard, numCopies, source) in newReturns)
            {
                var amountIndex = sortedCards.BinarySearch(returnCard, cardComparer);
                bool cardExists = amountIndex >= 0;

                var card = cardExists ? sortedCards[amountIndex] : returnCard;

                if (!cardExists)
                {
                    amountIndex = ~amountIndex;
                }

                var addPosition = positions.ElementAtOrDefault(amountIndex);
                var boxIndex = boundaries.BinarySearch(addPosition);

                if (boxIndex < 0)
                {
                    boxIndex = Math.Min(~boxIndex, sortedBoxes.Count - 1);
                }

                var boxOptions = sortedBoxes.Skip(boxIndex);
                var dividedBoxes = DivideToBoxes(boxOptions, boxSpace, numCopies);

                foreach (var (box, splitAmount) in dividedBoxes)
                {
                    yield return (card, splitAmount, source, box);
                }
            }
        }


        private IEnumerable<int> GetAddPositions(IEnumerable<Amount> boxAmounts)
        {
            int amountSum = 0;

            foreach (var shared in boxAmounts)
            {
                yield return amountSum;

                amountSum += shared.NumCopies;
            }
        }


        private IEnumerable<int> GetBoxBoundaries(IEnumerable<Box> boxes)
        {
            int capacitySum = 0;

            foreach (var box in boxes)
            {
                capacitySum += box.Capacity;

                yield return capacitySum;
            }
        }


        public async Task<Transaction?> OptimizeAsync(CancellationToken cancel = default)
        {
            try
            {
                await _lock.WaitAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            try
            {
                var sortedAmounts = await SortedAmounts.ToListAsync(cancel); // unbounded: keep eye on

                if (cancel.IsCancellationRequested)
                {
                    return null;
                }

                var sortedBoxes = await SortedBoxes.ToListAsync(cancel); // unbounded: keep eye on

                if (cancel.IsCancellationRequested)
                {
                    return null;
                }

                var optimizeChanges = GetOptimizeChanges(sortedAmounts, sortedBoxes);

                if (optimizeChanges == null)
                {
                    return null;
                }

                // intentionally throw db exception
                await _dbContext.SaveChangesAsync(cancel);

                if (cancel.IsCancellationRequested)
                {
                    return null;
                }

                return optimizeChanges;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }


        private Transaction? GetOptimizeChanges(
            IReadOnlyList<Amount> sharedAmounts, 
            IReadOnlyList<Box> sortedBoxes)
        {
            if (!sharedAmounts.Any() || !sortedBoxes.Any())
            {
                return null;
            }

            var (transaction, updatedAmounts) = AddUpdatedBoxAmounts(sharedAmounts, sortedBoxes);

            if (!transaction.Changes.Any())
            {
                return null;
            }

            var addedAmounts = updatedAmounts.Except(sharedAmounts);
            var emptyAmounts = updatedAmounts.Where(ca => ca.NumCopies == 0);

            _dbContext.Transactions.Add(transaction);
            _dbContext.Changes.AddRange(transaction.Changes); // just for clarity

            _dbContext.Amounts.AttachRange(addedAmounts);
            _dbContext.Amounts.RemoveRange(emptyAmounts);

            return transaction;
        }


        private (Transaction, IReadOnlyCollection<Amount>) AddUpdatedBoxAmounts(
            IReadOnlyList<Amount> sortedAmounts, 
            IReadOnlyList<Box> sortedBoxes)
        {
            var newTransaction = new Transaction();

            var boxAmounts = sortedAmounts
                .ToDictionary(ca => (ca.CardId, ca.LocationId));

            var oldAmounts = sortedAmounts
                .Select(ca => (ca.Card, ca.Location, ca.NumCopies))
                .ToList();

            foreach (var shared in sortedAmounts)
            {
                shared.NumCopies = 0;
            }

            var boxSpace = sortedBoxes // faster than summing every time
                .ToDictionary(b => b.Id, _ => 0);

            int boxStart = 0;

            foreach (var (card, oldBox, oldNumCopies) in oldAmounts)
            {
                var boxOptions = sortedBoxes
                    .Concat(sortedBoxes)
                    .Skip(boxStart)
                    .Take(sortedBoxes.Count);

                var newBoxAmounts = DivideToBoxes(boxOptions, boxSpace, oldNumCopies);

                bool multipleBoxes = false;

                foreach (var (newBox, newNumCopies) in newBoxAmounts)
                {
                    var boxAmount = GetOrAddBoxAmount(boxAmounts, card, newBox);

                    boxAmount.NumCopies += newNumCopies;
                    boxSpace[newBox.Id] += newNumCopies;

                    if (!multipleBoxes)
                    {
                        multipleBoxes = true;
                    }
                    else
                    {
                        boxStart++;
                    }

                    if (oldBox == newBox)
                    {
                        continue;
                    }

                    newTransaction.Changes.Add(new()
                    {
                        Card = card,
                        From = oldBox,
                        To = newBox,
                        Amount = newNumCopies
                    });
                }
            }

            return (newTransaction, boxAmounts.Values);
        }


        private IEnumerable<(Box Box, int Amount)> FitToBoxes(
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

                yield return (box, newNumCopies);

                cardsToAssign -= newNumCopies;

                if (cardsToAssign == 0)
                {
                    yield break;
                }
            }
        }


        private IEnumerable<(Box, int)> DivideToBoxes(
            IEnumerable<Box> boxes,
            IReadOnlyDictionary<int, int> boxSpace,
            int cardsToAssign)
        {
            var fitInBoxes = FitToBoxes(boxes.SkipLast(1), boxSpace, cardsToAssign);

            foreach (var fit in fitInBoxes)
            {
                cardsToAssign -= fit.Amount;

                yield return fit;

                if (cardsToAssign == 0)
                {
                    yield break;
                }
            }

            yield return (boxes.Last(), cardsToAssign);
        }


        private Amount GetOrAddBoxAmount(
            IDictionary<(string, int), Amount> boxAmounts, Card card, Box box)
        {
            var cardBox = (card.Id, box.Id);

            if (!boxAmounts.TryGetValue(cardBox, out var boxAmount))
            {
                boxAmount = new()
                {
                    Card = card,
                    Location = box
                };

                boxAmounts.Add(cardBox, boxAmount);
            }

            return boxAmount;
        }
    }
}