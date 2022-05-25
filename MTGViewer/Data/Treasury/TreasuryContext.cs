using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

namespace MTGViewer.Data.Treasury;

internal sealed partial class TreasuryContext
{
    private readonly CardDbContext _dbContext;
    private readonly Dictionary<LocationIndex, StorageSpace> _storageSpaces;

    private readonly HashSet<Box> _available;
    private readonly HashSet<Box> _overflow;

    private readonly Dictionary<HoldIndex, Hold> _holds;
    private readonly Dictionary<ChangeIndex, Change> _changes;

    private readonly Transaction _transaction;

    public TreasuryContext(
        CardDbContext dbContext,
        Dictionary<LocationIndex, StorageSpace> spaces)
    {
        AddMissingExcess(dbContext);

        var boxes = dbContext.Boxes.Local.OfType<Box>();
        var excess = dbContext.Excess.Local.OfType<Excess>();

        AddMissingStorageSpaces(spaces, boxes, excess);

        _dbContext = dbContext;

        _storageSpaces = spaces;

        _available = boxes
            .Where(b => _storageSpaces.GetValueOrDefault((LocationIndex)b)
                is StorageSpace { HasSpace: true })
            .ToHashSet();

        _overflow = boxes
            .Where(b => _storageSpaces.GetValueOrDefault((LocationIndex)b)
                is StorageSpace space && space.Held > space.Capacity)
            .ToHashSet();

        _holds = boxes
            .SelectMany(b => b.Holds)
            .Concat(excess
                .SelectMany(e => e.Holds))
            .ToDictionary(h => (HoldIndex)h);

        if (dbContext.Transactions.Local.FirstOrDefault() is Transaction transaction)
        {
            _transaction = transaction;
        }
        else
        {
            _transaction = new Transaction();
            _dbContext.Transactions.Attach(_transaction);
        }

        _changes = dbContext.Changes.Local
            .Where(c => c.TransactionId == _transaction.Id)
            .ToDictionary(c => (ChangeIndex)c);
    }

    public IReadOnlyDictionary<LocationIndex, StorageSpace> StorageSpaces => _storageSpaces;

    public IReadOnlyCollection<Box> Available => _available;
    public IReadOnlyCollection<Box> Overflow => _overflow;

    public IEnumerable<Excess> Excess => _dbContext.Excess.Local;

    public IReadOnlyCollection<Hold> Holds => _holds.Values;

    private static void AddMissingExcess(CardDbContext dbContext)
    {
        if (!dbContext.Excess.Local.Any())
        {
            var excessBox = Data.Excess.Create();

            dbContext.Excess.Add(excessBox);
        }
    }

    private static void AddMissingStorageSpaces(
        IDictionary<LocationIndex, StorageSpace> spaces,
        IEnumerable<Box> boxes,
        IEnumerable<Excess> excess)
    {
        var allStorage = boxes
            .Cast<Storage>()
            .Concat(excess);

        foreach (var storage in allStorage)
        {
            var index = (LocationIndex)storage;

            if (!spaces.ContainsKey(index))
            {
                spaces.Add(index, new StorageSpace
                {
                    Id = storage.Id,
                    Name = storage.Name,
                    Held = storage.Holds.Sum(h => h.Copies),
                    Capacity = (storage as Box)?.Capacity
                });
            }
        }
    }

    public void Deconstruct(
        out IReadOnlyCollection<Box> available,
        out IReadOnlyCollection<Box> overflow,
        out IEnumerable<Excess> excess,
        out IReadOnlyDictionary<LocationIndex, StorageSpace> storageSpace)
    {
        available = _available;
        overflow = _overflow;
        excess = _dbContext.Excess.Local;
        storageSpace = _storageSpaces;
    }

    public async Task LoadEntireStorageAsync(CancellationToken cancel)
    {
        await EntireStorage().LoadAsync(cancel);

        // holds are fully loaded, so the local hold copy sum is indeed the held amount

        var trackedStorage = _dbContext.Boxes.Local
            .OfType<Storage>()
            .Concat(_dbContext.Excess.Local
                .OfType<Storage>());

        foreach (var storage in trackedStorage)
        {
            var index = (LocationIndex)storage;

            if (!_storageSpaces.TryGetValue(index, out var space))
            {
                space = new StorageSpace
                {
                    Id = storage.Id,
                    Name = storage.Name,
                    Held = storage.Holds.Sum(h => h.Copies),
                    Capacity = (storage as Box)?.Capacity
                };

                _storageSpaces.Add(index, space);
            }

            if (storage is not Box box)
            {
                continue;
            }

            if (space.HasSpace)
            {
                _available.Add(box);
            }
            else if (space.Held > space.Capacity)
            {
                _overflow.Add(box);
            }
        }

        foreach (var hold in _dbContext.Holds.Local)
        {
            if (hold.Location is not Storage)
            {
                continue;
            }

            var index = (HoldIndex)hold;

            if (!_holds.ContainsKey(index))
            {
                _holds.Add(index, hold);
            }
        }
    }

    private IQueryable<Location> EntireStorage()
    {
        // loading all shared cards, could be memory inefficient
        // TODO: find more efficient way to determining card position
        // unbounded: keep eye on

        return _dbContext.Locations
            .Where(l => l is Excess || l is Box)

            .Include(l => l.Holds
                .OrderBy(h => h.Card.Name)
                    .ThenByDescending(h => h.Copies))
                .ThenInclude(h => h.Card)

            .OrderBy(l => l.Id);
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
        var index = new HoldIndex(card.Id, (LocationIndex)storage);

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
        var index = new ChangeIndex(
            card.Id, (LocationIndex)to, from is null ? null : (LocationIndex)from);

        if (!_changes.TryGetValue(index, out var change))
        {
            change = new Change
            {
                To = to,
                From = from,
                Card = card,
                Transaction = _transaction
            };

            _dbContext.Changes.Attach(change);
            _changes.Add(index, change);
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
        var index = (LocationIndex)storage;

        if (!_storageSpaces.TryGetValue(index, out var space))
        {
            throw new ArgumentException("Specified Storage's space is unknown", nameof(storage));
        }

        int newCopies = checked(space.Held + copies);

        if (newCopies < 0)
        {
            throw new ArgumentException("Amount of removed copies is too high", nameof(copies));
        }

        space.Held = newCopies;

        if (storage is not Box box)
        {
            return;
        }

        if (_available.Contains(box))
        {
            if (newCopies >= box.Capacity)
            {
                _available.Remove(box);
            }

            if (newCopies > box.Capacity)
            {
                _overflow.Add(box);
            }
        }
        else if (_overflow.Contains(box))
        {
            if (newCopies <= box.Capacity)
            {
                _overflow.Remove(box);
            }

            if (newCopies < box.Capacity)
            {
                _available.Add(box);
            }
        }
    }
}
