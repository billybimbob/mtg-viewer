using System.Linq;
using System.Collections.Generic;

namespace MTGViewer.Data.Internal;

internal static class ExcessExtensions
{
    public static void LowerExactExcess(this TreasuryContext treasuryContext)
    {
        new ExactExcess(treasuryContext).TransferExcess();
    }

    public static void LowerApproximateExcess(this TreasuryContext treasuryContext)
    {
        new ApproximateExcess(treasuryContext).TransferExcess();
    }
}


internal abstract class ExcessHandler
{
    protected TreasuryContext TreasuryContext { get; }

    protected ExcessHandler(TreasuryContext treasuryContext)
    {
        TreasuryContext = treasuryContext;
    }

    protected abstract IEnumerable<StorageAssignment<Hold>> GetAssignments();

    public void TransferExcess()
    {
        foreach ((Hold source, int copies, Storage target) in GetAssignments())
        {
            TreasuryContext.TransferCopies(source.Card, copies, target, source.Location);
        }
    }
}


internal class ExactExcess : ExcessHandler
{
    private ILookup<string, Storage>? _exactMatches;

    public ExactExcess(TreasuryContext treasuryContext) : base(treasuryContext)
    { }


    protected override IEnumerable<StorageAssignment<Hold>> GetAssignments()
    {
        var (available, _, excessStorage, _) = TreasuryContext;
        var excessHolds = excessStorage.SelectMany(b => b.Holds);

        if (!available.Any() || excessHolds.All(h => h.Copies == 0))
        {
            yield break;
        }

        foreach (var excess in excessHolds)
        {
            foreach (var assignment in FitToBoxes(excess))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<Hold>> FitToBoxes(Hold excess)
    {
        _exactMatches ??= AddLookup();

        var bestBoxes = _exactMatches[excess.CardId];
        var storageSpace = TreasuryContext.StorageSpace;

        return Assignment.FitToBoxes(excess, excess.Copies, bestBoxes, storageSpace);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var (available, _, excessStorage, storageSpace) = TreasuryContext;

        var availableHolds = available.SelectMany(b => b.Holds);

        var excessCards = excessStorage
            .SelectMany(b => b.Holds)
            .Select(h => h.Card);

        // TODO: account for changing Copies while iter
        return Assignment.ExactAddLookup(availableHolds, excessCards, storageSpace);
    }
}


internal class ApproximateExcess : ExcessHandler
{
    private ILookup<string, Storage>? _approxMatches;

    public ApproximateExcess(TreasuryContext treasuryContext) : base(treasuryContext)
    { }


    protected override IEnumerable<StorageAssignment<Hold>> GetAssignments()
    {
        var (available, _, excessStorage, _) = TreasuryContext;
        var excessHolds = excessStorage.SelectMany(b => b.Holds);

        if (!available.Any() || excessHolds.All(h => h.Copies == 0))
        {
            yield break;
        }

        foreach (var excess in excessHolds)
        {
            foreach (var assignment in FitToBoxes(excess))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<Hold>> FitToBoxes(Hold excess)
    {
        _approxMatches ??= AddLookup();

        var (available, _, _, storageSpace) = TreasuryContext;
        var bestBoxes = _approxMatches[excess.Card.Name].Union(available);

        return Assignment.FitToBoxes(excess, excess.Copies, bestBoxes, storageSpace);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var (available, _, excessStorage, storageSpace) = TreasuryContext;

        var availableHolds = available.SelectMany(b => b.Holds);

        var excessCards = excessStorage
            .SelectMany(b => b.Holds)
            .Select(h => h.Card);

        // TODO: account for changing Copies while iter
        return Assignment.ApproxAddLookup(availableHolds, excessCards, storageSpace);
    }
}
