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


        public IQueryable<Box> Shares => _dbContext.Boxes;


        public async Task ReturnAsync(IEnumerable<(Card, int numCopies)> returns)
        {
            if (InvalidReturns(returns))
            {
                return;
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
            return !returns.Any()
                || returns.Any(cn => cn.card == null)
                || returns.Any(cn => cn.numCopies <= 0);
        }


        private async Task<IReadOnlyList<(Card, int)>> ReturnExistingAsync(
            IEnumerable<(Card card, int)> returning)
        {
            var returnIds = returning
                .Select(ra => ra.card.Id)
                .ToArray();

            var returnAmounts = await _dbContext.Amounts
                .Where(ca => ca.Location is Box && returnIds.Contains(ca.CardId))
                .ToListAsync();

            if (!returnAmounts.Any())
            {
                return returning.ToList();
            }

            var returnGroups = returnAmounts
                .GroupBy(ca => ca.CardId,
                    (_, amounts) => new SameCardGroup(amounts))
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


        private async Task<IReadOnlyList<CardAmount>> GetSortedAmountsAsync()
        {
            // loading all shared cards, could be memory inefficient
            // TODO: find more efficient way to determining card position

            return await _dbContext.Amounts
                .Where(ca => ca.Location is Box)
                .Include(ca => ca.Location)
                .Include(ca => ca.Card)
                .OrderBy(ca => ca.Card.Name)
                    .ThenBy(ca => ca.Card.SetName)
                .ToListAsync();
        }


        private IReadOnlyList<Box> GetSortedBoxes(IReadOnlyList<CardAmount> boxAmounts)
        {
            return boxAmounts
                .Select(ca => ca.Location)
                .Distinct()
                .Cast<Box>()
                .OrderBy(s => s.Id)
                .ToList();
        }


        private async Task ReturnNewAsync(IEnumerable<(Card, int)> newReturns)
        {
            var sortedSharedAmounts = await GetSortedAmountsAsync();

            if (!sortedSharedAmounts.Any())
            {
                throw new DbUpdateException("There are no possible boxes to return the cards to");
            }

            var sortedBoxes = GetSortedBoxes(sortedSharedAmounts);

            var cardIndices = new List<int>(sortedSharedAmounts.Count);
            var cardCount = 0;

            foreach (var shared in sortedSharedAmounts)
            {
                cardIndices.Add( cardCount );
                cardCount += shared.Amount;
            }

            foreach (var (card, numCopies) in newReturns)
            {
                var amountIndex = FindAmountIndex(sortedSharedAmounts, card);
                var cardIndex = cardIndices[amountIndex];
                var boxIndex = Math.Min(cardIndex / _boxSize, sortedBoxes.Count - 1);

                var newSpot = new CardAmount
                {
                    Card = card,
                    Location = sortedBoxes[boxIndex],
                    Amount = numCopies
                };

                _dbContext.Amounts.Attach(newSpot);
            }
        }


        private int FindAmountIndex(IReadOnlyList<CardAmount> sortedAmounts, Card card)
        {
            // TODO: use binary search
            var amountIndex = -1;

            foreach (var sharedAmount in sortedAmounts)
            {
                var sharedCard = sharedAmount.Card;

                var nameCompare = string.Compare(
                    sharedCard.Name, card.Name, StringComparison.InvariantCulture);

                var setCompare = string.Compare(
                    sharedCard.SetName, card.SetName, StringComparison.InvariantCulture);

                if (nameCompare > 0 || nameCompare == 0 && setCompare > 0)
                {
                    break;
                }

                amountIndex++;
            }

            return Math.Max(0, amountIndex);
        }



        public async Task OptimizeAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var sortedSharedAmounts = await GetSortedAmountsAsync();
                var sortedBoxes = GetSortedBoxes(sortedSharedAmounts);

                AddUpdatedBoxAmounts(sortedSharedAmounts, sortedBoxes);

                _dbContext.Amounts.RemoveRange(
                    sortedSharedAmounts.Where(ca => ca.Amount == 0));

                // intentionally throw db exception
                await _dbContext.SaveChangesAsync();
            }
            finally
            {
                _lock.Release();
            }
        }


        private void AddUpdatedBoxAmounts(
            IReadOnlyList<CardAmount> sharedAmounts, IReadOnlyList<Box> boxes)
        {
            var oldAmounts = sharedAmounts
                .Select(ca => (ca.Card, ca.Amount))
                .ToList();

            foreach (var shared in sharedAmounts)
            {
                shared.Amount = 0;
            }

            var amountMap = sharedAmounts
                .ToDictionary(ca => (ca.CardId, ca.LocationId));

            var cardIndex = 0;

            foreach (var (card, oldNumCopies) in oldAmounts)
            {
                var boxAmounts = DivideToBoxAmounts(boxes, cardIndex, oldNumCopies);

                foreach (var (box, newNumCopies) in boxAmounts)
                {
                    var cardBox = (card.Id, box.Id);

                    if (!amountMap.TryGetValue(cardBox, out var boxAmount))
                    {
                        boxAmount = new CardAmount
                        {
                            Card = card,
                            Location = box
                        };

                        _dbContext.Attach(boxAmount);
                        amountMap.Add(cardBox, boxAmount);
                    }

                    boxAmount.Amount += newNumCopies;
                }

                cardIndex += oldNumCopies;
            }
        }


        private IEnumerable<(Box, int)> DivideToBoxAmounts(
            IReadOnlyList<Box> boxes, int cardIndex, int numCopies)
        {
            var amounts = new List<int>{ _boxSize - cardIndex % _boxSize };
            var givenAmounts = _boxSize;

            while (givenAmounts + _boxSize < numCopies)
            {
                amounts.Add(_boxSize);
                givenAmounts += _boxSize;
            }

            var startBox = cardIndex / _boxSize;
            var endBox = (cardIndex + numCopies) / _boxSize;

            if (startBox != endBox)
            {
                amounts.Add((cardIndex + numCopies) % _boxSize);
            }

            return boxes
                .Skip(startBox)
                .Take(endBox - startBox + 1)
                .Zip(amounts);
        }
    }
}