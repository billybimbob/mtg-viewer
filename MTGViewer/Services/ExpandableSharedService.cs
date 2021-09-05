using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MTGViewer.Data;


namespace MTGViewer.Services
{
    public class ExpandableSharedService : ISharedStorage
    {
        private readonly int _boxSize;
        private readonly CardDbContext _dbContext;

        public ExpandableSharedService(IConfiguration config, CardDbContext dbContext)
        {
            _dbContext = dbContext;

            if (!int.TryParse(config["boxSize"], out _boxSize))
            {
                _boxSize = 80;
            }
        }




        public async Task ReturnAsync(IEnumerable<(Card, int)> returning)
        {
            if (!returning.Any())
            {
                return;
            }

            var newReturns = await ReturnExistingAsync(returning);

            if (newReturns.Any())
            {
                await ReturnNewAsync(newReturns);
            }

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            { }
        }


        private async Task<IReadOnlyList<(Card, int)>> ReturnExistingAsync(
            IEnumerable<(Card card, int)> returning)
        {
            var returnIds = returning
                .Select(ra => ra.card.Id)
                .ToArray();

            var returnAmounts = await _dbContext.Amounts
                .Where(ca => ca.Location is Shared && returnIds.Contains(ca.CardId))
                .ToListAsync();

            var returnGroups = returnAmounts
                .GroupBy(ca => ca.CardId,
                    (_, amounts) => new AmountGroup(amounts))
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


        private async Task ReturnNewAsync(IEnumerable<(Card, int)> newReturns)
        {
            // loading all shared cards, could be memory inefficient
            // TODO: find more efficient way to determining card position

            var sortedSharedAmounts = await _dbContext.Amounts
                .Where(ca => ca.Location is Shared)
                .Include(ca => ca.Location)
                .Include(ca => ca.Card)
                .OrderBy(ca => ca.Card.Name)
                    .ThenBy(ca => ca.Card.SetName)
                .ToListAsync();

            var sortedBoxes = sortedSharedAmounts
                .Select(ca => ca.Location)
                .Distinct()
                .Cast<Shared>()
                .OrderBy(s => s.Id)
                .ToList();

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
            var sortedSharedAmounts = await _dbContext.Amounts
                .Where(ca => ca.Location is Shared)
                .Include(ca => ca.Location)
                .Include(ca => ca.Card)
                .OrderBy(ca => ca.Card.Name)
                    .ThenBy(ca => ca.Card.SetName)
                .ToListAsync();

            var sortedBoxes = sortedSharedAmounts
                .Select(ca => ca.Location)
                .Distinct()
                .Cast<Shared>()
                .OrderBy(s => s.Id)
                .ToList();

            AddNewBoxAmounts(sortedSharedAmounts, sortedBoxes);

            _dbContext.Amounts.RemoveRange(
                sortedSharedAmounts.Where(ca => ca.Amount == 0));

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            { }
        }


        private void AddNewBoxAmounts(
            IReadOnlyList<CardAmount> sharedAmounts, IReadOnlyList<Shared> boxes)
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


        private IEnumerable<(Shared, int)> DivideToBoxAmounts(
            IReadOnlyList<Shared> boxes, int cardIndex, int numCopies)
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