using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MTGViewer.Data;

namespace MTGViewer.Services.Treasury;


internal sealed class TreasuryContext
{
    private readonly Dictionary<Box, int> _boxSpace;

    private readonly HashSet<Box> _available;
    private readonly HashSet<Box> _overflow;
    private readonly IReadOnlyList<Box> _excess;

    private readonly Amount[] _originalAmounts;
    private readonly Dictionary<(string, Box), Amount> _amountMap;

    public TreasuryContext(
        IReadOnlyList<Box> bounded, IReadOnlyList<Box> excess)
    {
        if (!bounded.Any() || bounded.Any(b => b.IsExcess))
        {
            throw new ArgumentException(nameof(bounded));
        }

        if (!excess.Any() || excess.Any(b => !b.IsExcess))
        {
            throw new ArgumentException(nameof(excess));
        }

        var boxes = bounded.Concat(excess);

        _boxSpace = boxes
            .ToDictionary(
                b => b, b => b.Cards.Sum(a => a.NumCopies));

        _available = bounded
            .Where(b => _boxSpace.GetValueOrDefault(b) < b.Capacity)
            .ToHashSet();

        _overflow = bounded
            .Where(b => _boxSpace.GetValueOrDefault(b) > b.Capacity)
            .ToHashSet();

        _excess = excess;

        _originalAmounts = boxes
            .SelectMany(b => b.Cards)
            .ToArray();

        _amountMap = _originalAmounts
            .Where(a => a.Location is Box)
            .ToDictionary(
                a => (a.CardId, (Box)a.Location));
    }

    public IReadOnlyCollection<Box> Available => _available;
    public IReadOnlyCollection<Box> Overflow => _overflow;

    public IReadOnlyList<Box> Excess => _excess;
    public IReadOnlyDictionary<Box, int> BoxSpace => _boxSpace;

    public IEnumerable<Amount> AddedAmounts() => 
        _amountMap.Values.Except(_originalAmounts);

    public void Deconstruct(
        out IReadOnlyCollection<Box> available,
        out IReadOnlyCollection<Box> overflow,
        out IReadOnlyList<Box> excess,
        out IReadOnlyDictionary<Box, int> boxSpace)
    {
        available = _available;
        overflow = _overflow;
        excess = _excess;
        boxSpace = _boxSpace;
    }


    public void ReturnCopies(Card card, Box box, int numCopies)
    {
        if (!_boxSpace.TryGetValue(box, out int boxSize))
        {
            throw new ArgumentException(nameof(box));
        }

        var index = (card.Id, box);

        if (!_amountMap.TryGetValue(index, out var amount))
        {
            // avoid dbContext tracking the added amount so that 
            // the primary key will not be set
            // there is also no prop fixup, so all props fully specified

            _amountMap[index] = new Amount
            {
                CardId = card.Id,
                Card = card,
                LocationId = box.Id,
                Location = box,
                NumCopies = numCopies
            };
        }
        else
        {
            amount.NumCopies += numCopies;
        }

        boxSize += numCopies;

        _boxSpace[box] = boxSize;
        UpdateBoxSets(box, boxSize);
    }


    private void UpdateBoxSets(Box box, int newSize)
    {
        if (box.IsExcess)
        {
            return;
        }

        if (_available.Contains(box))
        {
            if (newSize >= box.Capacity)
            {
                _available.Remove(box);
            }

            if (newSize > box.Capacity)
            {
                _overflow.Add(box);
            }
        }
        else if (_overflow.Contains(box))
        {
            if (newSize <= box.Capacity)
            {
                _overflow.Remove(box);
            }
            
            if (newSize < box.Capacity)
            {
                _available.Add(box);
            }
        }
    }
}