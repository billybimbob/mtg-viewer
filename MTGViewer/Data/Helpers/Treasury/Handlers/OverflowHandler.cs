using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Internal;

internal static class OverflowExtensions
{
    public static void LowerExactOver(this TreasuryContext treasuryContext)
    {
        new ExactOverflow(treasuryContext).TransferOverflow();
    }

    public static void LowerApproximateOver(this TreasuryContext treasuryContext)
    {
        new ApproximateOverflow(treasuryContext).TransferOverflow();
    }
}


internal abstract class OverflowHandler
{
    protected TreasuryContext TreasuryContext { get; }

    protected OverflowHandler(TreasuryContext treasuryContext)
    {
        TreasuryContext = treasuryContext;
    }

    protected abstract IEnumerable<StorageAssignment<Hold>> GetAssignments();

    public void TransferOverflow()
    {
        foreach ((Hold source, int copies, Storage storage) in GetAssignments())
        {
            TreasuryContext.TransferCopies(source.Card, copies, storage, source.Location);
        }
    }
}


internal class ExactOverflow : OverflowHandler
{
    private ILookup<string, Storage>? _exactMatches;

    public ExactOverflow(TreasuryContext treasuryContext)
        : base(treasuryContext)
    { }


    protected override IEnumerable<StorageAssignment<Hold>> GetAssignments()
    {
        var (available, overflowBoxes, _, _) = TreasuryContext;

        if (!available.Any() || !overflowBoxes.Any())
        {
            yield break;
        }

        var overflowHolds = overflowBoxes.SelectMany(b => b.Holds);

        foreach (var source in overflowHolds)
        {
            foreach (var assignment in OverflowAssignment(source))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<Hold>> OverflowAssignment(Hold source)
    {
        _exactMatches ??= AddLookup();

        var bestBoxes = _exactMatches[source.CardId];

        if (!bestBoxes.Any())
        {
            return Enumerable.Empty<StorageAssignment<Hold>>();
        }

        if (source.Location is not Box sourceBox)
        {
            return Enumerable.Empty<StorageAssignment<Hold>>();
        }

        var storageSpace = TreasuryContext.StorageSpace;

        int copiesAbove = storageSpace.GetValueOrDefault(sourceBox) - sourceBox.Capacity;
        if (copiesAbove <= 0)
        {
            return Enumerable.Empty<StorageAssignment<Hold>>();
        }

        int minTransfer = Math.Min(source.Copies, copiesAbove);

        return Assignment.FitToBoxes(source, minTransfer, bestBoxes, storageSpace);
    }


    private ILookup<string, Storage> AddLookup()
    {
        var (available, overflowBoxes, _, storageSpace) = TreasuryContext;

        var availableHolds = available.SelectMany(b => b.Holds);

        var overflowCards = overflowBoxes
            .SelectMany(b => b.Holds)
            .Select(h => h.Card);

        return Assignment.ExactAddLookup(availableHolds, overflowCards, storageSpace);
    }
}


internal class ApproximateOverflow : OverflowHandler
{
    private ILookup<string, Storage>? _approxMatches;

    public ApproximateOverflow(TreasuryContext treasuryContext)
        : base(treasuryContext)
    { }


    protected override IEnumerable<StorageAssignment<Hold>> GetAssignments()
    {
        var overflowBoxes = TreasuryContext.Overflow;

        if (!overflowBoxes.Any())
        {
            yield break;
        }

        var overflowHolds = overflowBoxes.SelectMany(b => b.Holds);

        foreach (var source in overflowHolds)
        {
            foreach (var assignment in OverflowAssignment(source))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<Hold>> OverflowAssignment(Hold source)
    {
        _approxMatches ??= AddLookup();

        if (source.Location is not Box sourceBox)
        {
            return Enumerable.Empty<StorageAssignment<Hold>>();
        }

        var (available, _, excessStorage, storageSpace) = TreasuryContext;

        int copiesAbove = storageSpace.GetValueOrDefault(sourceBox) - sourceBox.Capacity;
        if (copiesAbove <= 0)
        {
            return Enumerable.Empty<StorageAssignment<Hold>>();
        }

        var bestBoxes = _approxMatches[source.Card.Name]
            .Union(available)
            .Concat(excessStorage);

        int minTransfer = Math.Min(source.Copies, copiesAbove);

        return Assignment.FitToBoxes(source, minTransfer, bestBoxes, storageSpace);
    }


    private ILookup<string, Storage> AddLookup()
    {
        var (available, overflowBoxes, _, storageSpace) = TreasuryContext;

        var availableHolds = available.SelectMany(b => b.Holds);

        var overflowCards = overflowBoxes
            .SelectMany(b => b.Holds)
            .Select(h => h.Card);

        return Assignment.ApproxAddLookup(availableHolds, overflowCards, storageSpace);
    }
}
