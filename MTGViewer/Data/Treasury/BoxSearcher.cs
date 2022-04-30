using System;
using System.Collections.Generic;
using System.Linq;
using MTGViewer.Utils;

namespace MTGViewer.Data.Treasury;

internal sealed class BoxSearcher
{
    private readonly IReadOnlyCollection<Box> _unorderedBoxes;

    private readonly List<Box> _orderedBoxes;
    private readonly List<Card> _orderedCards;

    private readonly Dictionary<string, int> _firstCards;

    private readonly List<int> _boxBoundaries;
    private readonly List<int> _addPositions;

    private bool _isFullyLoaded;

    public BoxSearcher(IReadOnlyCollection<Box> boxes)
    {
        int totalHolds = boxes
            .SelectMany(b => b.Holds)
            .Count();

        int totalCards = boxes
            .SelectMany(b => b.Holds, (_, h) => h.CardId)
            .Distinct()
            .Count();

        _unorderedBoxes = boxes;
        _isFullyLoaded = false;

        _orderedBoxes = new List<Box>(boxes.Count);
        _orderedCards = new List<Card>(totalHolds);

        _firstCards = new Dictionary<string, int>(totalCards);

        _boxBoundaries = new List<int>(boxes.Count);
        _addPositions = new List<int>(totalHolds);
    }

    public static IEnumerable<TValue> GetOrderedRequests<TValue>(
        IEnumerable<TValue> values,
        Func<TValue, Card> getCard)
    {
        // descending to account for the static order of the searching algorithm
        // the first added cards do not shift down the positioning of the order cards,
        // so each of the returned cards should have less effect on following returns
        // keep eye on

        return values
            .OrderByDescending(c => getCard.Invoke(c).Name)
                .ThenByDescending(c => getCard.Invoke(c).SetName);
    }

    public IEnumerable<Box> FindBestBoxes(Card card)
    {
        InitOrderedValues();

        int cardSearch = _orderedCards.BinarySearch(card, CardNameComparer.Instance);

        int cardIndex = cardSearch >= 0
            ? _firstCards.GetValueOrDefault(_orderedCards[cardSearch].Id)
            : ~cardSearch;

        int addPosition = _addPositions.ElementAtOrDefault(cardIndex);
        int boxSearch = _boxBoundaries.BinarySearch(addPosition);

        int boxIndex = boxSearch >= 0
            ? boxSearch
            : Math.Min(~boxSearch, _orderedBoxes.Count - 1);

        foreach (var box in _orderedBoxes.Skip(boxIndex))
        {
            yield return box;
        }
    }

    private void InitOrderedValues()
    {
        if (_isFullyLoaded)
        {
            return;
        }

        _orderedBoxes.AddRange(_unorderedBoxes.OrderBy(b => b.Id));

        var sortedHolds = _orderedBoxes
            .SelectMany(b => b.Holds)
            .OrderBy(h => h.Card.Name)
                .ThenBy(h => h.Card.SetName)
            .ToList();

        _orderedCards.AddRange(sortedHolds.Select(h => h.Card));

        foreach ((string id, int index) in GetFirstCards(_orderedCards))
        {
            _firstCards.Add(id, index);
        }

        _boxBoundaries.AddRange(GetBoxBoundaries(_orderedBoxes));
        _addPositions.AddRange(GetAddPositions(sortedHolds));

        _isFullyLoaded = true;
    }

    private static IEnumerable<(string id, int index)> GetFirstCards(IEnumerable<Card> sortedCards)
    {
        return sortedCards
            .Select((card, index) => (card, index))
            .GroupBy(ci => ci.card.Id,
                (id, cis) => (id, cis.First().index));
    }

    private static IEnumerable<int> GetBoxBoundaries(IEnumerable<Box> boxes)
    {
        int capacitySum = 0;

        foreach (var box in boxes)
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

        foreach (var hold in boxHolds)
        {
            yield return holdTotal;

            checked
            {
                holdTotal += hold.Copies;
            }
        }
    }
}
