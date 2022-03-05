using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Internal;

internal sealed class TreasuryContext
{
    private readonly CardDbContext _dbContext;
    private readonly Dictionary<Storage, int> _storageSpace;

    private readonly HashSet<Box> _available;
    private readonly HashSet<Box> _overflow;
    private readonly IReadOnlyList<Excess> _excess;

    private readonly Dictionary<(string, Storage), Amount> _amounts;
    private readonly Dictionary<(string, Location, Location?), Change> _changes;

    private readonly Transaction _transaction;

    public TreasuryContext(CardDbContext dbContext)
    {
        var boxes = dbContext.Boxes.Local.OfType<Box>();
        var excess = dbContext.Excess.Local.OfType<Excess>();

        if (!excess.Any())
        {
            throw new ArgumentException("There are no excess boxes");
        }

        _dbContext = dbContext;

        _storageSpace = boxes
            .Cast<Storage>()
            .Concat(excess)
            .ToDictionary(s => s, s => s.Cards.Sum(a => a.NumCopies));

        _available = boxes
            .Where(b => _storageSpace.GetValueOrDefault(b) < b.Capacity)
            .ToHashSet();

        _overflow = boxes
            .Where(b => _storageSpace.GetValueOrDefault(b) > b.Capacity)
            .ToHashSet();

        _excess = excess.ToList();

        _amounts = boxes
            .SelectMany(b => b.Cards)
            .Concat(excess
                .SelectMany(e => e.Cards))
            .Where(a => a.Location is Storage)

            .ToDictionary(
                a => (a.CardId, (Storage)a.Location));

        if (dbContext.Transactions.Local.FirstOrDefault() is Transaction transaction)
        {
            _transaction = transaction;
        }
        else
        {
            _transaction = new();
            _dbContext.Transactions.Attach(_transaction);
        }

        _changes = dbContext.Changes.Local
            .Where(c => c.TransactionId == _transaction.Id)
            .ToDictionary(c => (c.CardId, c.To, c.From));
    }

    public IReadOnlyCollection<Box> Available => _available;
    public IReadOnlyCollection<Box> Overflow => _overflow;

    public IReadOnlyList<Excess> Excess => _excess;
    public IReadOnlyDictionary<Storage, int> StorageSpace => _storageSpace;

    public IReadOnlyCollection<Amount> Amounts => _amounts.Values;


    public void Deconstruct(
        out IReadOnlyCollection<Box> available,
        out IReadOnlyCollection<Box> overflow,
        out IReadOnlyList<Excess> excess,
        out IReadOnlyDictionary<Storage, int> storageSpace)
    {
        available = _available;
        overflow = _overflow;
        excess = _excess;
        storageSpace = _storageSpace;
    }


    public void AddCopies(Card card, int numCopies, Storage storage)
    {
        UpdateAmount(card, numCopies, storage);
        UpdateChange(card, numCopies, storage, null);
        UpdateStorageSpace(storage, numCopies);
    }


    public void TransferCopies(Card card, int numCopies, Storage to, Location from)
    {
        if (to == from)
        {
            return;
        }

        UpdateAmount(card, numCopies, to);
        UpdateStorageSpace(to, numCopies);

        if (from is Storage fromStorage)
        {
            UpdateAmount(card, -numCopies, fromStorage);
            UpdateStorageSpace(fromStorage, -numCopies);
        }

        UpdateChange(card, numCopies, to, from);
    }


    public void TransferCopies(Card card, int numCopies, Location to, Storage from)
    {
        if (to == from)
        {
            return;
        }

        UpdateChange(card, numCopies, to, from);

        if (to is Storage toStorage)
        {
            UpdateAmount(card, numCopies, toStorage);
            UpdateStorageSpace(toStorage, numCopies);
        }

        UpdateAmount(card, -numCopies, from);
        UpdateStorageSpace(from, -numCopies);
    }


    private void UpdateAmount(Card card, int numCopies, Storage storage)
    {
        var index = (card.Id, storage);

        if (!_amounts.TryGetValue(index, out var amount))
        {
            amount = new Amount
            {
                Card = card,
                Location = storage
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


    private void UpdateStorageSpace(Storage storage, int numCopies)
    {
        if (!_storageSpace.TryGetValue(storage, out int boxSize))
        {
            throw new ArgumentException(nameof(storage));
        }

        checked
        {
            boxSize += numCopies;
        }

        if (boxSize < 0)
        {
            throw new ArgumentException(nameof(numCopies));
        }

        _storageSpace[storage] = boxSize;

        if (storage is not Box box)
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
