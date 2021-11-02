using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MTGViewer.Data;

#nullable enable

namespace MTGViewer.Services
{
    public sealed class FlatVariableStorage : ITreasury, IDisposable
    {
        private readonly int _boxSize;
        private readonly CardDbContext _dbContext;
        private readonly SemaphoreSlim _lock; // needed since CardDbContext is not thread safe
        private readonly ILogger<FlatVariableStorage> _logger;

        public FlatVariableStorage(IConfiguration config, CardDbContext dbContext, ILogger<FlatVariableStorage> logger)
        {
            _boxSize = config.GetValue("BoxSize", 80);
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

        public IQueryable<CardAmount> Cards => 
            _dbContext.Amounts
                .Where(ca => ca.Location is Box)
                .AsNoTrackingWithIdentityResolution();


        private IQueryable<Box> SortedBoxes =>
            // unbounded: keep eye on
            _dbContext.Boxes.OrderBy(s => s.Id);


        private IQueryable<CardAmount> SortedAmounts =>
            // loading all shared cards, could be memory inefficient
            // TODO: find more efficient way to determining card position
            // unbounded: keep eye on

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

                var newTransaction = new Transaction();

                _dbContext.Transactions.Add(newTransaction);

                var newReturns = await ReturnExistingAsync(newTransaction, returns, cancel);

                cancel.ThrowIfCancellationRequested();

                if (newReturns.Any())
                {
                    await ReturnNewAsync(newTransaction, newReturns, cancel);

                    cancel.ThrowIfCancellationRequested();
                }

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


        private async Task<IReadOnlyList<CardReturn>> ReturnExistingAsync(
            Transaction transaction, IEnumerable<CardReturn> returning,
            CancellationToken cancel)
        {
            var returnIds = returning
                .Select(cr => cr.Card.Id)
                .Distinct()
                .ToArray();

            var returnAmounts = await _dbContext.Amounts
                .Where(ca => ca.Location is Box && returnIds.Contains(ca.CardId))
                .Include(ca => ca.Location)
                .ToListAsync(cancel); // unbounded: keep eye on

            cancel.ThrowIfCancellationRequested();

            if (!returnAmounts.Any())
            {
                // intentionally make new copy
                return returning.ToList();
            }

            var returnTargets = returnAmounts
                // TODO: divide evenly among all options
                .GroupBy(ca => ca.CardId, (_, amounts) => amounts.First())
                .ToDictionary(ag => ag.CardId);

            var changes = new Dictionary<(string, int), Change>();

            foreach (var (card, numCopies, deck) in returning)
            {
                if (!returnTargets.TryGetValue(card.Id, out var target))
                {
                    continue;
                }

                var changeKey = (card.Id, deck?.Id ?? 0);

                if (!changes.TryGetValue(changeKey, out var change))
                {
                    change = new()
                    {
                        Card = card,
                        To = target.Location,
                        From = deck,
                        Amount = 0,
                        Transaction = transaction
                    };

                    changes.Add(changeKey, change);
                }

                target.Amount += numCopies;
                change.Amount += numCopies;
            }

            _dbContext.Changes.AttachRange(changes.Values);

            return returning
                .Where(cr => !returnTargets.ContainsKey(cr.Card.Id))
                .ToList();
        }


        private async Task ReturnNewAsync(
            Transaction transaction,
            IReadOnlyList<CardReturn> newReturns,
            CancellationToken cancel)
        {
            var sortedBoxes = await SortedBoxes.ToListAsync(cancel);

            cancel.ThrowIfCancellationRequested();

            if (!sortedBoxes.Any())
            {
                throw new InvalidOperationException(
                    "There are no possible boxes to return the cards to");
            }

            var sortedSharedAmounts = await SortedAmounts.ToListAsync(cancel);

            cancel.ThrowIfCancellationRequested();

            var returnPairs = FindNewReturnPairs(newReturns, sortedSharedAmounts, sortedBoxes);

            foreach (var (card, numCopies, source, box) in returnPairs)
            {
                var newSpot = new CardAmount
                {
                    Card = card,
                    Location = box,
                    Amount = numCopies
                };

                var newChange = new Change
                {
                    Card = card,
                    To = box,
                    From = source,
                    Amount = numCopies,
                    Transaction = transaction
                };

                _dbContext.Amounts.Attach(newSpot);
                _dbContext.Changes.Attach(newChange);
            }
        }


        private IEnumerable<(Card, int, Deck?, Box)> FindNewReturnPairs(
            IReadOnlyList<CardReturn> newReturns,
            IReadOnlyList<CardAmount> boxAmounts,
            IReadOnlyList<Box> boxes)
        {
            var cardComparer = new CardNameComparer();

            var sortedCards = boxAmounts
                .Select(ca => ca.Card)
                .ToList();

            var positions = GetAddPositions(boxAmounts).ToList();
            var boundaries = GetBoxBoundaries(boxes).ToList();

            var combinedReturns = newReturns
                .GroupBy(cr => (cr.Card, cr.Deck),
                    (cd, crs) => 
                        (cd.Card, crs.Sum(cr => cr.NumCopies), cd.Deck));
            
            foreach (var (card, numCopies, source) in combinedReturns)
            {
                var amountIndex = sortedCards.BinarySearch(card, cardComparer);

                if (amountIndex < 0)
                {
                    amountIndex = ~amountIndex;
                }

                var addPosition = positions.ElementAtOrDefault(amountIndex);
                var boxIndex = boundaries.BinarySearch(addPosition);

                if (boxIndex < 0)
                {
                    boxIndex = Math.Min(~boxIndex, boxes.Count - 1);
                }

                // TODO: split across multiple boxes if it does not fit
                var box = boxes[boxIndex];

                yield return (card, numCopies, source, box);
            }
        }


        private IEnumerable<int> GetAddPositions(IEnumerable<CardAmount> boxAmounts)
        {
            int amountSum = 0;

            foreach (var shared in boxAmounts)
            {
                yield return amountSum;

                amountSum += shared.Amount;
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
                var sortedBoxAmounts = await SortedAmounts.ToListAsync(cancel);

                if (cancel.IsCancellationRequested)
                {
                    return null;
                }

                var sortedBoxes = await SortedBoxes.ToListAsync(cancel);

                if (cancel.IsCancellationRequested)
                {
                    return null;
                }

                var newTransaction = AddUpdateBoxAmounts(sortedBoxAmounts, sortedBoxes);

                if (!ValidateChanges(newTransaction, sortedBoxAmounts))
                {
                    return null;
                }

                // intentionally throw db exception
                await _dbContext.SaveChangesAsync(cancel);

                if (cancel.IsCancellationRequested)
                {
                    return null;
                }

                return newTransaction;
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


        private Transaction AddUpdateBoxAmounts(
            IReadOnlyList<CardAmount> sharedAmounts, IReadOnlyList<Box> boxes)
        {
            var newTransaction = new Transaction();

            var oldAmounts = sharedAmounts
                .Select(ca => (ca.Card, ca.Location, ca.Amount))
                .ToList();

            foreach (var shared in sharedAmounts)
            {
                shared.Amount = 0;
            }

            var boxSpaces = boxes // faster than summing every time
                .ToDictionary(b => b.Id, _ => 0);

            var amountMap = sharedAmounts
                .ToDictionary(ca => (ca.CardId, ca.LocationId));

            int boxStart = 0;

            foreach (var (card, oldBox, oldNumCopies) in oldAmounts)
            {
                var boxOptions = boxes.Skip(boxStart);
                var newBoxAmounts = DivideToBoxes(boxOptions, boxSpaces, oldNumCopies);

                bool multipleBoxes = false;

                foreach (var (newBox, newNumCopies) in newBoxAmounts)
                {
                    var boxAmount = FindOrAddBoxAmount(amountMap, card, newBox);

                    boxAmount.Amount += newNumCopies;
                    boxSpaces[newBox.Id] += newNumCopies;

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

            return newTransaction;
        }


        private IEnumerable<(Box, int)> DivideToBoxes(
            IEnumerable<Box> boxes,
            IReadOnlyDictionary<int, int> boxSpaces,
            int cardsToAssign)
        {
            foreach (var newBox in boxes.SkipLast(1))
            {
                int boxSpace = boxSpaces[newBox.Id];
                int remainingSpace = Math.Max(0, newBox.Capacity - boxSpace);

                int newNumCopies = Math.Min(cardsToAssign, remainingSpace);

                if (newNumCopies == 0)
                {
                    continue;
                }

                yield return (newBox, newNumCopies);

                cardsToAssign -= newNumCopies;

                if (cardsToAssign == 0)
                {
                    yield break;
                }
            }

            yield return (boxes.Last(), cardsToAssign);
        }


        private CardAmount FindOrAddBoxAmount(
            IDictionary<(string, int), CardAmount> boxAmounts, Card card, Box box)
        {
            var cardBox = (card.Id, box.Id);

            if (!boxAmounts.TryGetValue(cardBox, out var boxAmount))
            {
                boxAmount = new()
                {
                    Card = card,
                    Location = box
                };

                _dbContext.Amounts.Attach(boxAmount);
                boxAmounts.Add(cardBox, boxAmount);
            }

            return boxAmount;
        }


        private bool ValidateChanges(
            Transaction optimizeChanges, IReadOnlyList<CardAmount> boxAmounts)
        {
            if (!optimizeChanges.Changes.Any())
            {
                return false;
            }

            var emptyAmounts = boxAmounts.Where(ca => ca.Amount == 0);

            _dbContext.Amounts.RemoveRange(emptyAmounts);
            _dbContext.Transactions.Add(optimizeChanges);

            return true;
        }
    }
}