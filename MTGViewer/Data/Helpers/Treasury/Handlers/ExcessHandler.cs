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
    protected readonly TreasuryContext _treasuryContext;

    public ExcessHandler(TreasuryContext treasuryContext)
    {
        _treasuryContext = treasuryContext;
    }

    protected abstract IEnumerable<BoxAssignment<Amount>> GetAssignments();

    public void TransferExcess()
    {
        foreach ((Amount source, int numCopies, Box box) in GetAssignments())
        {
            _treasuryContext.TransferCopies(source.Card, numCopies, box, source.Location);
        }
    }
}


internal class ExactExcess : ExcessHandler
{
    private ILookup<string, Box>? _exactMatches;

    public ExactExcess(TreasuryContext treasuryContext) : base(treasuryContext)
    { }


    protected override IEnumerable<BoxAssignment<Amount>> GetAssignments()
    {
        var (available, _, excessBoxes, _) = _treasuryContext;
        var excessAmounts = excessBoxes.SelectMany(b => b.Cards);

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


    private IEnumerable<BoxAssignment<Amount>> FitToBoxes(Amount excess)
    {
        _exactMatches ??= AddLookup();

        var bestBoxes = _exactMatches[excess.CardId];
        var boxSpace = _treasuryContext.BoxSpace;

        return Assignment.FitToBoxes(excess, excess.NumCopies, bestBoxes, boxSpace);
    }

    private ILookup<string, Box> AddLookup()
    {
        var (available, _, excessBoxes, boxSpace) = _treasuryContext;

        var availableAmounts = available.SelectMany(b => b.Cards);

        var excessCards = excessBoxes
            .SelectMany(b => b.Cards)
            .Select(a => a.Card);

        // TODO: account for changing NumCopies while iter
        return Assignment.ExactAddLookup(availableAmounts, excessCards, boxSpace);
    }
}


internal class ApproximateExcess : ExcessHandler
{
    private ILookup<string, Box>? _approxMatches;

    public ApproximateExcess(TreasuryContext treasuryContext) : base(treasuryContext)
    { }


    protected override IEnumerable<BoxAssignment<Amount>> GetAssignments()
    {
        var (available, _, excessBoxes, _) = _treasuryContext;
        var excessAmounts = excessBoxes.SelectMany(b => b.Cards);

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


    private IEnumerable<BoxAssignment<Amount>> FitToBoxes(Amount excess)
    {
        _approxMatches ??= AddLookup();

        var (available, _, _, boxSpace) = _treasuryContext;
        var bestBoxes = _approxMatches[excess.Card.Name].Union(available);

        return Assignment.FitToBoxes(excess, excess.NumCopies, bestBoxes, boxSpace);
    }

    private ILookup<string, Box> AddLookup()
    {
        var (available, _, excessBoxes, boxSpace) = _treasuryContext;

        var availableAmounts = available.SelectMany(b => b.Cards);

        var excessCards = excessBoxes
            .SelectMany(b => b.Cards)
            .Select(a => a.Card);

        // TODO: account for changing NumCopies while iter
        return Assignment.ApproxAddLookup(availableAmounts, excessCards, boxSpace);
    }
}
