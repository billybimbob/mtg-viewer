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
        foreach ((var source, int copies, var storage) in GetAssignments())
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
        var (available, overflow, _, _) = TreasuryContext;

        if (!available.Any() || !overflow.Any())
        {
            yield break;
        }

        var overflowHolds = overflow.SelectMany(b => b.Holds);

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
        var storageSpaces = TreasuryContext.StorageSpaces;

        var index = (LocationIndex)source.Location;

        if (storageSpaces.GetValueOrDefault(index)
            is not { Remaining: < 0 and int deficit })
        {
            return Enumerable.Empty<StorageAssignment<Hold>>();
        }

        _exactMatches ??= AddLookup();

        var matches = _exactMatches[source.CardId];

        if (!matches.Any())
        {
            return Enumerable.Empty<StorageAssignment<Hold>>();
        }

        int minTransfer = Math.Min(source.Copies, Math.Abs(deficit));

        return Assignment.FitToStorage(source, minTransfer, matches, storageSpaces);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var (available, overflowBoxes, _, _) = TreasuryContext;

        var targets = available.SelectMany(b => b.Holds);

        var overflowCards = overflowBoxes
            .SelectMany(b => b.Holds)
            .Select(h => h.Card);

        return Assignment.ExactAddLookup(targets, overflowCards);
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
        var (available, _, excess, storageSpaces) = TreasuryContext;

        var index = (LocationIndex)source.Location;

        if (storageSpaces.GetValueOrDefault(index)
            is not { Remaining: < 0 and int deficit })
        {
            return Enumerable.Empty<StorageAssignment<Hold>>();
        }

        _approxMatches ??= AddLookup();

        var matches = _approxMatches[source.Card.Name]
            .Union(available)
            .Concat(excess);

        if (!matches.Any())
        {
            return Enumerable.Empty<StorageAssignment<Hold>>();
        }

        int minTransfer = Math.Min(source.Copies, Math.Abs(deficit));

        return Assignment.FitToStorage(source, minTransfer, matches, storageSpaces);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var (available, overflow, _, _) = TreasuryContext;

        var targets = available.SelectMany(b => b.Holds);

        var overflowCards = overflow
            .SelectMany(b => b.Holds)
            .Select(h => h.Card);

        return Assignment.ApproxAddLookup(targets, overflowCards);
    }
}
