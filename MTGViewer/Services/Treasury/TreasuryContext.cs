using System;
using System.Collections.Generic;
using System.Linq;
using MTGViewer.Data;

namespace MTGViewer.Services.Internal;

internal sealed class TreasuryContext
{
    private readonly CardDbContext _dbContext;
    private readonly Dictionary<Box, int> _boxSpace;

    private readonly HashSet<Box> _available;
    private readonly HashSet<Box> _overflow;
    private readonly IReadOnlyList<Box> _excess;

    private readonly Dictionary<(string, Box), Amount> _amounts;
    private readonly Dictionary<(string, Location, Location?), Change> _changes;

    private readonly Transaction _transaction;

    public TreasuryContext(CardDbContext dbContext)
    {
        var boxes = dbContext.Boxes.Local.AsEnumerable();

        if (!boxes.Any(b => b.IsExcess) || !boxes.Any(b => b.IsExcess))
        {
            throw new ArgumentException(nameof(boxes));
        }

        _dbContext = dbContext;

        _boxSpace = boxes
            .ToDictionary(
                b => b, b => b.Cards.Sum(a => a.NumCopies));

        _available = boxes
            .Where(b => !b.IsExcess && _boxSpace.GetValueOrDefault(b) < b.Capacity)
            .ToHashSet();

        _overflow = boxes
            .Where(b => !b.IsExcess && _boxSpace.GetValueOrDefault(b) > b.Capacity)
            .ToHashSet();

        _excess = boxes
            .Where(b => b.IsExcess)
            .ToList();

        _amounts = boxes
            .SelectMany(b => b.Cards)
            .Where(a => a.Location is Box)
            .ToDictionary(
                a => (a.CardId, (Box)a.Location));

        _changes = dbContext.Changes.Local
            .ToDictionary(c => (c.CardId, c.To, c.From));

        if (dbContext.Transactions.Local.FirstOrDefault() is Transaction transaction)
        {
            _transaction = transaction;
        }
        else
        {
            _transaction = new();
            _dbContext.Transactions.Attach(_transaction);
        }
    }

    public IReadOnlyCollection<Box> Available => _available;
    public IReadOnlyCollection<Box> Overflow => _overflow;

    public IReadOnlyList<Box> Excess => _excess;
    public IReadOnlyDictionary<Box, int> BoxSpace => _boxSpace;

    public IReadOnlyCollection<Amount> Amounts => _amounts.Values;


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


    public void AddCopies(Card card, int numCopies, Box box)
    {
        UpdateAmount(card, numCopies, box);
        UpdateChange(card, numCopies, box, null);
        UpdateBoxSpace(box, numCopies);
    }


    public void TransferCopies(Card card, int numCopies, Box to, Location from)
    {
        if (to == from)
        {
            return;
        }

        UpdateAmount(card, numCopies, to);
        UpdateBoxSpace(to, numCopies);

        if (from is Box fromBox)
        {
            UpdateAmount(card, -numCopies, fromBox);
            UpdateBoxSpace(fromBox, -numCopies);
        }

        UpdateChange(card, numCopies, to, from);
    }


    public void TransferCopies(Card card, int numCopies, Location to, Box from)
    {
        if (to == from)
        {
            return;
        }

        UpdateChange(card, numCopies, to, from);

        if (to is Box toBox)
        {
            UpdateAmount(card, numCopies, toBox);
            UpdateBoxSpace(toBox, numCopies);
        }

        UpdateAmount(card, -numCopies, from);
        UpdateBoxSpace(from, -numCopies);
    }


    private void UpdateAmount(Card card, int numCopies, Box box)
    {
        var index = (card.Id, box);

        if (!_amounts.TryGetValue(index, out var amount))
        {
            amount = new Amount
            {
                Card = card,
                Location = box
            };

            _dbContext.Amounts.Attach(amount);
            _amounts.Add(index, amount);
        }

        int newCopies = checked(amount.NumCopies + numCopies);
        if (newCopies < 0)
        {
            throw new ArgumentException(nameof(numCopies));
        }

        amount.NumCopies = newCopies;
    }


    private void UpdateChange(Card card, int amount, Location to, Location? from)
    {
        var changeIndex = (card.Id, to, from);

        if (!_changes.TryGetValue(changeIndex, out var change))
        {
            change = new Change
            {
                To = to,
                From = from,
                Card = card,
                Transaction = _transaction
            };

            _dbContext.Changes.Attach(change);
            _changes.Add(changeIndex, change);
        }

        int newAmount = checked(change.Amount + amount);
        if (newAmount < 0)
        {
            throw new ArgumentException(nameof(amount));
        }

        change.Amount = newAmount;
    }


    private void UpdateBoxSpace(Box box, int numCopies)
    {
        if (!_boxSpace.TryGetValue(box, out int boxSize))
        {
            throw new ArgumentException(nameof(box));
        }

        checked
        {
            boxSize += numCopies;
        }

        if (boxSize < 0)
        {
            throw new ArgumentException(nameof(numCopies));
        }

        _boxSpace[box] = boxSize;

        if (box.IsExcess)
        {
            return;
        }

        if (_available.Contains(box))
        {
            if (boxSize >= box.Capacity)
            {
                _available.Remove(box);
            }

            if (boxSize > box.Capacity)
            {
                _overflow.Add(box);
            }
        }
        else if (_overflow.Contains(box))
        {
            if (boxSize <= box.Capacity)
            {
                _overflow.Remove(box);
            }
            
            if (boxSize < box.Capacity)
            {
                _available.Add(box);
            }
        }
    }
}