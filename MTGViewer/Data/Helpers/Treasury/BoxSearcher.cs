using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Internal;

internal sealed class BoxSearcher
{
    private readonly List<Box> _sortedBoxes;

    private readonly List<Card> _sortedCards;
    private readonly Dictionary<string, int> _firstCards;

    private readonly List<int> _boxBoundaries;
    private readonly List<int> _addPositions;


    public BoxSearcher(IReadOnlyCollection<Box> boxes)
    {
        _sortedBoxes = boxes
            .OrderBy(b => b.Id)
            .ToList();

        var sortedHolds = _sortedBoxes
            .SelectMany(b => b.Holds)
            .OrderBy(h => h.Card.Name)
                .ThenBy(h => h.Card.SetName)
            .ToList();

        _sortedCards = sortedHolds
            .Select(h => h.Card)
            .ToList();

        _firstCards = GetFirstCards(_sortedCards);

        _boxBoundaries = GetBoxBoundaries(_sortedBoxes).ToList();
        _addPositions = GetAddPositions(sortedHolds).ToList();
    }


    private static Dictionary<string, int> GetFirstCards(IEnumerable<Card> sortedCards)
    {
        return sortedCards
            .Select((card, index) => (card, index))
            .GroupBy(ci => ci.card.Id,
                (id, cis) => (id, cis.First().index))

            .ToDictionary(
                ii => ii.id, ii => ii.index);
    }

    private static IEnumerable<int> GetBoxBoundaries(IEnumerable<Box> boxes)
    {
        int capacitySum = 0;

        foreach (Box box in boxes)
        {
            checked
            {
                capacitySum += box.Capacity;
            }

            yield return capacitySum;
        }
    }

    private static IEnumerable<int> GetAddPositions(IEnumerable<Hold> boxHolds)
    {
        int holdTotal = 0;

        foreach (Hold hold in boxHolds)
        {
            yield return holdTotal;

            checked
            {
                holdTotal += hold.Copies;
            }
        }
    }


    public IEnumerable<Box> FindBestBoxes(Card card)
    {
        int cardSearch = _sortedCards.BinarySearch(card, CardNameComparer.Instance);

        int cardIndex = cardSearch >= 0
            ? _firstCards.GetValueOrDefault(_sortedCards[cardSearch].Id)
            : ~cardSearch;

        int addPosition = _addPositions.ElementAtOrDefault(cardIndex);
        int boxSearch = _boxBoundaries.BinarySearch(addPosition);

        int boxIndex = boxSearch >= 0
            ? boxSearch
            : Math.Min(~boxSearch, _sortedBoxes.Count - 1);

        return _sortedBoxes.Skip(boxIndex);
    }
}
