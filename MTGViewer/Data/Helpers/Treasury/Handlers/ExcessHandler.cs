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

    protected abstract IEnumerable<StorageAssignment<Amount>> GetAssignments();

    public void TransferExcess()
    {
        foreach ((Amount source, int numCopies, Storage target) in GetAssignments())
        {
            TreasuryContext.TransferCopies(source.Card, numCopies, target, source.Location);
        }
    }
}


internal class ExactExcess : ExcessHandler
{
    private ILookup<string, Storage>? _exactMatches;

    public ExactExcess(TreasuryContext treasuryContext) : base(treasuryContext)
    { }


    protected override IEnumerable<StorageAssignment<Amount>> GetAssignments()
    {
        var (available, _, excessStorage, _) = TreasuryContext;
        var excessAmounts = excessStorage.SelectMany(b => b.Cards);

        if (!available.Any() || excessAmounts.All(a => a.NumCopies == 0))
        {
            yield break;
        }

        foreach (var excess in excessAmounts)
        {
            foreach (var assignment in FitToBoxes(excess))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<Amount>> FitToBoxes(Amount excess)
    {
        _exactMatches ??= AddLookup();

        var bestBoxes = _exactMatches[excess.CardId];
        var boxSpace = TreasuryContext.StorageSpace;

        return Assignment.FitToBoxes(excess, excess.NumCopies, bestBoxes, boxSpace);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var (available, _, excessStorage, storageSpace) = TreasuryContext;

        var availableAmounts = available.SelectMany(b => b.Cards);

        var excessCards = excessStorage
            .SelectMany(b => b.Cards)
            .Select(a => a.Card);

        // TODO: account for changing NumCopies while iter
        return Assignment.ExactAddLookup(availableAmounts, excessCards, storageSpace);
    }
}


internal class ApproximateExcess : ExcessHandler
{
    private ILookup<string, Storage>? _approxMatches;

    public ApproximateExcess(TreasuryContext treasuryContext) : base(treasuryContext)
    { }


    protected override IEnumerable<StorageAssignment<Amount>> GetAssignments()
    {
        var (available, _, excessStorage, _) = TreasuryContext;
        var excessAmounts = excessStorage.SelectMany(b => b.Cards);

        if (!available.Any() || excessAmounts.All(a => a.NumCopies == 0))
        {
            yield break;
        }

        foreach (var excess in excessAmounts)
        {
            foreach (var assignment in FitToBoxes(excess))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<Amount>> FitToBoxes(Amount excess)
    {
        _approxMatches ??= AddLookup();

        var (available, _, _, storageSpace) = TreasuryContext;
        var bestBoxes = _approxMatches[excess.Card.Name].Union(available);

        return Assignment.FitToBoxes(excess, excess.NumCopies, bestBoxes, storageSpace);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var (available, _, excessBoxes, storageSpace) = TreasuryContext;

        var availableAmounts = available.SelectMany(b => b.Cards);

        var excessCards = excessBoxes
            .SelectMany(b => b.Cards)
            .Select(a => a.Card);

        // TODO: account for changing NumCopies while iter
        return Assignment.ApproxAddLookup(availableAmounts, excessCards, storageSpace);
    }
}
