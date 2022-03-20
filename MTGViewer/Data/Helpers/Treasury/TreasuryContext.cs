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

    private readonly Dictionary<(string, Storage), Hold> _holds;
    private readonly Dictionary<(string, Location, Location?), Change> _changes;

    private readonly Transaction _transaction;

    public TreasuryContext(CardDbContext dbContext)
    {
        var boxes = dbContext.Boxes.Local.OfType<Box>();
        var excess = dbContext.Excess.Local.OfType<Excess>();

        if (!excess.Any())
        {
            throw new InvalidOperationException("There are no excess boxes");
        }

        _dbContext = dbContext;

        _storageSpace = boxes
            .Cast<Storage>()
            .Concat(excess)
            .ToDictionary(s => s, s => s.Holds.Sum(h => h.Copies));

        _available = boxes
            .Where(b => _storageSpace.GetValueOrDefault(b) < b.Capacity)
            .ToHashSet();

        _overflow = boxes
            .Where(b => _storageSpace.GetValueOrDefault(b) > b.Capacity)
            .ToHashSet();

        _excess = excess.ToList();

        _holds = boxes
            .SelectMany(b => b.Holds)
            .Concat(excess
                .SelectMany(e => e.Holds))
            .Where(h => h.Location is Storage)

            .ToDictionary(
                h => (h.CardId, (Storage)h.Location));

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

    public IReadOnlyCollection<Hold> Holds => _holds.Values;


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


    public void AddCopies(Card card, int copies, Storage storage)
    {
        UpdateHold(card, copies, storage);
        UpdateChange(card, copies, storage, null);
        UpdateStorageSpace(storage, copies);
    }


    public void TransferCopies(Card card, int copies, Storage to, Location from)
    {
        if (to == from)
        {
            return;
        }

        UpdateHold(card, copies, to);
        UpdateStorageSpace(to, copies);

        if (from is Storage fromStorage)
        {
            UpdateHold(card, -copies, fromStorage);
            UpdateStorageSpace(fromStorage, -copies);
        }

        UpdateChange(card, copies, to, from);
    }


    public void TransferCopies(Card card, int copies, Location to, Storage from)
    {
        if (to == from)
        {
            return;
        }

        UpdateChange(card, copies, to, from);

        if (to is Storage toStorage)
        {
            UpdateHold(card, copies, toStorage);
            UpdateStorageSpace(toStorage, copies);
        }

        UpdateHold(card, -copies, from);
        UpdateStorageSpace(from, -copies);
    }


    private void UpdateHold(Card card, int copies, Storage storage)
    {
        var index = (card.Id, storage);

        if (!_holds.TryGetValue(index, out var hold))
        {
            hold = new Hold
            {
                Card = card,
                Location = storage
            };

            _dbContext.Holds.Attach(hold);
            _holds.Add(index, hold);
        }

        int newCopies = checked(hold.Copies + copies);
        if (newCopies < 0)
        {
            throw new ArgumentException("Amount of removed copies is too high", nameof(copies));
        }

        hold.Copies = newCopies;
    }


    private void UpdateChange(Card card, int copies, Location to, Location? from)
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

        int newCopies = checked(change.Copies + copies);
        if (newCopies < 0)
        {
            throw new ArgumentException("Amount of add copies is too low", nameof(copies));
        }

        change.Copies = newCopies;
    }


    private void UpdateStorageSpace(Storage storage, int copies)
    {
        if (!_storageSpace.TryGetValue(storage, out int boxSize))
        {
            throw new ArgumentException("Specified Storage's space is unknown", nameof(storage));
        }

        checked
        {
            boxSize += copies;
        }

        if (boxSize < 0)
        {
            throw new ArgumentException("Amount of removed copies is too high", nameof(copies));
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
