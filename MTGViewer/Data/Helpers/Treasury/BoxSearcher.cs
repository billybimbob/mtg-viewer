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

        var sortedAmounts = _sortedBoxes
            .SelectMany(b => b.Cards)
            .OrderBy(a => a.Card.Name)
                .ThenBy(a => a.Card.SetName)
            .ToList();

        _sortedCards = sortedAmounts
            .Select(a => a.Card)
            .ToList();

        _firstCards = GetFirstCards(_sortedCards);

        _boxBoundaries = GetBoxBoundaries(_sortedBoxes).ToList();
        _addPositions = GetAddPositions(sortedAmounts).ToList();
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

    private static IEnumerable<int> GetAddPositions(IEnumerable<Amount> boxAmounts)
    {
        int amountSum = 0;

        foreach (Amount amount in boxAmounts)
        {
            yield return amountSum;

            checked
            {
                amountSum += amount.Copies;
            }
        }
    }


    public IEnumerable<Box> FindBestBoxes(Card card)
    {
        int cardSearch = _sortedCards.BinarySearch(card, CardNameComparer.Instance);

        int cardIndex = cardSearch >= 0
            ? _firstCards.GetValueOrDefault( _sortedCards[cardSearch].Id )
            : ~cardSearch;

        int addPosition = _addPositions.ElementAtOrDefault(cardIndex);
        int boxSearch = _boxBoundaries.BinarySearch(addPosition);

        int boxIndex = boxSearch >= 0
            ? boxSearch
            : Math.Min(~boxSearch, _sortedBoxes.Count - 1);

        return _sortedBoxes.Skip(boxIndex);
    }
}
