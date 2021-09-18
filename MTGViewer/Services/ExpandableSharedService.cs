using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MTGViewer.Data;


namespace MTGViewer.Services
{
    public class ExpandableSharedService : ISharedStorage, IDisposable
    {
        private readonly int _boxSize;
        private readonly CardDbContext _dbContext;
        // needed since CardDbContext is not thread safe
        private readonly SemaphoreSlim _lock;

        public ExpandableSharedService(IConfiguration config, CardDbContext dbContext)
        {
            _dbContext = dbContext;

            if (!int.TryParse(config["boxSize"], out _boxSize))
            {
                _boxSize = 80;
            }

            _lock = new(1, 1);
        }


        public void Dispose()
        {
            _dbContext.Dispose();
            _lock.Dispose();
        }


        public IQueryable<Box> Boxes =>
            _dbContext.Boxes.AsNoTrackingWithIdentityResolution();

        public IQueryable<BoxAmount> Cards =>
            _dbContext.BoxAmounts.AsNoTrackingWithIdentityResolution();


        public async Task ReturnAsync(IEnumerable<(Card, int numCopies)> returns)
        {
            if (!returns.Any())
            {
                return;
            }

            if (InvalidReturns(returns))
            {
                throw new ArgumentException("Given returns are invalid");
            }

            await _lock.WaitAsync();
            try
            {
                var newReturns = await ReturnExistingAsync(returns);

                if (newReturns.Any())
                {
                    await ReturnNewAsync(newReturns);
                }

                // intentionally throw db exception
                await _dbContext.SaveChangesAsync();
            }
            finally
            {
                _lock.Release();
            }
        }


        private bool InvalidReturns(IEnumerable<(Card card, int numCopies)> returns)
        {
            return returns.Any(cn => cn.card == null)
                || returns.Any(cn => cn.numCopies <= 0);
        }


        private async Task<IReadOnlyList<(Card, int)>> ReturnExistingAsync(
            IEnumerable<(Card card, int)> returning)
        {
            var returnIds = returning
                .Select(ra => ra.card.Id)
                .ToArray();

            var returnAmounts = await _dbContext.BoxAmounts
                .Where(ba => returnIds.Contains(ba.CardId))
                .ToListAsync();

            if (!returnAmounts.Any())
            {
                return returning.ToList();
            }

            var returnGroups = returnAmounts
                .GroupBy(ca => ca.CardId,
                    (_, amounts) => new CardGroup(amounts))
                .ToDictionary(ag => ag.CardId);

            foreach (var (card, numCopies) in returning)
            {
                if (returnGroups.TryGetValue(card.Id, out var group))
                {
                    group.Amount += numCopies;
                }
            }

            return returning
                .Where(ra => !returnGroups.ContainsKey(ra.card.Id))
                .ToList();
        }


        private async Task<IReadOnlyList<BoxAmount>> GetSortedAmountsAsync()
        {
            // loading all shared cards, could be memory inefficient
            // TODO: find more efficient way to determining card position

            return await _dbContext.BoxAmounts
                .Include(ca => ca.Card)
                .OrderBy(ca => ca.Card.Name)
                    .ThenBy(ca => ca.Card.SetName)
                .ToListAsync();
        }


        private async Task<IReadOnlyList<Box>> GetSortedBoxesAsync()
        {
            return await _dbContext.Boxes
                .OrderBy(s => s.Id)
                .ToListAsync();
        }


        private async Task ReturnNewAsync(IEnumerable<(Card card, int numCopies)> newReturns)
        {
            var sortedBoxes = await GetSortedBoxesAsync();

            if (!sortedBoxes.Any())
            {
                throw new InvalidOperationException("There are no possible boxes to return the cards to");
            }

            var sortedSharedAmounts = await GetSortedAmountsAsync();
            var cardIndices = GetCardIndices(sortedSharedAmounts);

            var returnGroups = newReturns
                .GroupBy(ci => ci.card, (card, cis) =>
                    (card, numCopies: cis.Sum(ci => ci.numCopies)) );

            foreach (var (card, numCopies) in returnGroups)
            {
                var amountIndex = FindAmountIndex(sortedSharedAmounts, card);

                var cardIndex = cardIndices.ElementAtOrDefault(amountIndex);
                var boxIndex = Math.Min(cardIndex / _boxSize, sortedBoxes.Count - 1);

                var newSpot = new BoxAmount
                {
                    Card = card,
                    Location = sortedBoxes[boxIndex],
                    Amount = numCopies
                };

                _dbContext.BoxAmounts.Attach(newSpot);
            }
        }


        private IReadOnlyList<int> GetCardIndices(IEnumerable<BoxAmount> boxAmounts)
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
            var high = sortedAmounts.Count;

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



        public async Task OptimizeAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var sortedBoxAmounts = await GetSortedAmountsAsync();
                var sortedBoxes = await GetSortedBoxesAsync();

                AddUpdatedBoxAmounts(sortedBoxAmounts, sortedBoxes);

                _dbContext.BoxAmounts.RemoveRange(
                    sortedBoxAmounts.Where(ba => ba.Amount == 0));

                // intentionally throw db exception
                await _dbContext.SaveChangesAsync();
            }
            finally
            {
                _lock.Release();
            }
        }


        private void AddUpdatedBoxAmounts(
            IReadOnlyList<BoxAmount> sharedAmounts, IReadOnlyList<Box> boxes)
        {
            var oldAmounts = sharedAmounts
                .Select(ca => (ca.Card, ca.Amount))
                .ToList();

            foreach (var shared in sharedAmounts)
            {
                shared.Amount = 0;
            }

            var amountMap = sharedAmounts.ToDictionary(ca => (ca.CardId, ca.LocationId));
            var boxCounts = boxes.ToDictionary(b => b.Id, _ => 0); // faster than summing every time
            var cardIndex = 0;

            foreach (var (card, oldNumCopies) in oldAmounts)
            {
                var boxAmounts = DivideToBoxAmounts(boxes, boxCounts, cardIndex, oldNumCopies);

                foreach (var (box, newNumCopies) in boxAmounts)
                {
                    var cardBox = (card.Id, box.Id);

                    if (!amountMap.TryGetValue(cardBox, out var boxAmount))
                    {
                        boxAmount = new()
                        {
                            Card = card,
                            Location = box
                        };

                        _dbContext.BoxAmounts.Attach(boxAmount);
                        amountMap.Add(cardBox, boxAmount);
                    }

                    boxAmount.Amount += newNumCopies;
                    boxCounts[box.Id] += newNumCopies;
                }

                cardIndex += oldNumCopies;
            }
        }


        private IEnumerable<(Box, int)> DivideToBoxAmounts(
            IReadOnlyList<Box> boxes,
            IReadOnlyDictionary<int, int> boxCounts,
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
                var remainingSpace = Math.Max(0, _boxSize - boxCounts[box.Id]);
                var boxAmount = Math.Min(numCopies, remainingSpace);

                amounts.Add(boxAmount);
                numCopies -= boxAmount;
            }

            return boxRange.Zip(amounts).ToList();
        }
    }
}