using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MTGViewer.Data;

#nullable enable

namespace MTGViewer.Services
{
    public sealed class ExpandableSharedService : ISharedStorage, IDisposable
    {
        private readonly int _boxSize;
        private readonly CardDbContext _dbContext;
        // needed since CardDbContext is not thread safe
        private readonly SemaphoreSlim _lock;

        public ExpandableSharedService(IConfiguration config, CardDbContext dbContext)
        {
            _boxSize = config.GetValue("BoxSize", 80);
            _dbContext = dbContext;
            _lock = new(1, 1);
        }


        public void Dispose()
        {
            _lock.Dispose();
        }


        public IQueryable<Box> Boxes => _dbContext.Boxes
            .AsNoTrackingWithIdentityResolution();

        public IQueryable<CardAmount> Cards => _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .AsNoTrackingWithIdentityResolution();


        public async Task<Transaction> ReturnAsync(IEnumerable<CardReturn> returns)
        {
            if (InvalidReturns(returns))
            {
                throw new ArgumentException("Given returns are invalid");
            }


            await _lock.WaitAsync();
            try
            {
                var newTransaction = new Transaction();
                _dbContext.Transactions.Add(newTransaction);

                var newReturns = await ReturnExistingAsync(newTransaction, returns);

                if (newReturns.Any())
                {
                    await ReturnNewAsync(newTransaction, newReturns);
                }

                // intentionally leave db exception unhandled
                await _dbContext.SaveChangesAsync();

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
                || returns.Any(cn => cn.Card == null || cn.NumCopies <= 0);
        }


        private async Task<IReadOnlyList<CardReturn>> ReturnExistingAsync(
            Transaction transaction,
            IEnumerable<CardReturn> returning)
        {
            var returnIds = returning
                .Select(cr => cr.Card.Id)
                .ToArray();

            var returnAmounts = await _dbContext.Amounts
                .Where(ca => ca.Location is Box && returnIds.Contains(ca.CardId))
                .Include(ca => ca.Location)
                .ToListAsync(); // unbounded: keep eye on

            if (!returnAmounts.Any())
            {
                return returning.ToList();
            }

            var returnTargets = returnAmounts
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


        private async Task<IReadOnlyList<CardAmount>> GetSortedAmountsAsync()
        {
            // loading all shared cards, could be memory inefficient
            // TODO: find more efficient way to determining card position

            return await _dbContext.Amounts
                .Where(ca => ca.Location is Box)
                .Include(ca => ca.Card)
                .OrderBy(ca => ca.Card.Name)
                    .ThenBy(ca => ca.Card.SetName)
                .ToListAsync(); // unbounded: keep eye on
        }


        private async Task<IReadOnlyList<Box>> GetSortedBoxesAsync()
        {
            return await _dbContext.Boxes
                .OrderBy(s => s.Id)
                    // unbounded: keep eye on
                .ToListAsync();
        }


        private async Task ReturnNewAsync(
            Transaction transaction, IEnumerable<CardReturn> newReturns)
        {
            var sortedBoxes = await GetSortedBoxesAsync();

            if (!sortedBoxes.Any())
            {
                throw new InvalidOperationException(
                    "There are no possible boxes to return the cards to");
            }

            var sortedSharedAmounts = await GetSortedAmountsAsync();
            var cardIndices = GetCardIndices(sortedSharedAmounts);

            var combinedReturns = newReturns
                .GroupBy(
                    cr => (cr.Card, cr.Deck),
                    (cd, crs) => (cd.Card, crs.Sum(cr => cr.NumCopies), cd.Deck));

            foreach (var (card, numCopies, source) in combinedReturns)
            {
                var amountIndex = FindAmountIndex(sortedSharedAmounts, card);
                var cardIndex = cardIndices.ElementAtOrDefault(amountIndex);
                var boxIndex = Math.Min(cardIndex / _boxSize, sortedBoxes.Count - 1);

                var box = sortedBoxes[boxIndex];

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


        private IReadOnlyList<int> GetCardIndices(IEnumerable<CardAmount> boxAmounts)
        {
            var cardIndices = new List<int>(boxAmounts.Count());
            var cardCount = 0;

            foreach (var shared in boxAmounts)
            {
                cardIndices.Add( cardCount );
                cardCount += shared.Amount;
            }

            return cardIndices;
        }


        private int FindAmountIndex(IReadOnlyList<CardAmount> sortedAmounts, Card card)
        {
            var amountIndex = 0;

            if (!sortedAmounts.Any())
            {
                return amountIndex;
            }

            var low = 0;
            var high = sortedAmounts.Count - 1;

            while (low <= high)
            {
                amountIndex = (low + high) / 2;

                var boxCard = sortedAmounts[amountIndex].Card;

                var nameCompare = string.Compare(
                    card.Name, boxCard.Name, StringComparison.InvariantCulture);

                var setCompare = string.Compare(
                    card.SetName, boxCard.SetName, StringComparison.InvariantCulture);

                if (nameCompare == 0 && setCompare == 0)
                {
                    break;
                }

                if (nameCompare > 0 || nameCompare == 0 && setCompare > 0)
                {
                    low = amountIndex + 1;
                }
                else
                {
                    high = amountIndex - 1;
                }
            }

            return amountIndex;
        }



        public async Task<Transaction?> OptimizeAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var sortedBoxAmounts = await GetSortedAmountsAsync();
                var sortedBoxes = await GetSortedBoxesAsync();

                var newTransaction = AddUpdatedBoxAmounts(sortedBoxAmounts, sortedBoxes);

                if (!ValidateChanges(newTransaction, sortedBoxAmounts))
                {
                    return null;
                }

                // intentionally throw db exception
                await _dbContext.SaveChangesAsync();

                return newTransaction;
            }
            finally
            {
                _lock.Release();
            }
        }


        private Transaction AddUpdatedBoxAmounts(
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

            // faster than summing every time
            var boxSpace = boxes
                .ToDictionary(b => b.Id, _ => 0);

            var amountMap = sharedAmounts
                .ToDictionary(ca => (ca.CardId, ca.LocationId));

            var cardIndex = 0;


            foreach (var (card, oldBox, oldNumCopies) in oldAmounts)
            {
                var boxAmounts = DivideToBoxAmounts(boxes, boxSpace, cardIndex, oldNumCopies);

                foreach (var (newBox, newNumCopies) in boxAmounts)
                {
                    var boxAmount = FindOrAddBoxAmount(amountMap, card, newBox);

                    boxAmount.Amount += newNumCopies;
                    boxSpace[newBox.Id] += newNumCopies;

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

                cardIndex += oldNumCopies;
            }

            return newTransaction;
        }


        private IReadOnlyList<(Box, int)> DivideToBoxAmounts(
            IReadOnlyList<Box> boxes,
            IReadOnlyDictionary<int, int> boxSpace,
            int cardIndex,
            int numCopies)
        {
            var startBox = cardIndex / _boxSize;
            var endBox = (cardIndex + numCopies) / _boxSize;

            var numBoxes = endBox - startBox + 1;
            var amounts = new List<int>(numBoxes);

            var boxRange = boxes
                .Skip(startBox)
                .Take(numBoxes);

            foreach (var box in boxRange)
            {
                var remainingSpace = Math.Max(0, _boxSize - boxSpace[box.Id]);
                var boxAmount = Math.Min(numCopies, remainingSpace);

                amounts.Add(boxAmount);
                numCopies -= boxAmount;
            }

            return boxRange.Zip(amounts).ToList();
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