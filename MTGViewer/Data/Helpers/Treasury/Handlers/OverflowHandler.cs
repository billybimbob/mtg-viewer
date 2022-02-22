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
    protected readonly TreasuryContext _treasuryContext;

    public OverflowHandler(TreasuryContext treasuryContext)
    {
        _treasuryContext = treasuryContext;
    }

    protected abstract IEnumerable<BoxAssignment<Amount>> GetAssignments();

    public void TransferOverflow()
    {
        foreach ((Amount source, int numCopies, Box box) in GetAssignments())
        {
            _treasuryContext.TransferCopies(source.Card, numCopies, box, source.Location);
        }
    }
}


internal class ExactOverflow: OverflowHandler
{
    private ILookup<string, Box>? _exactMatches;

    public ExactOverflow(TreasuryContext treasuryContext)
        : base(treasuryContext)
    { }


    protected override IEnumerable<BoxAssignment<Amount>> GetAssignments()
    {
        var (available, overflowBoxes, _, _) = _treasuryContext;

        if (!available.Any() || !overflowBoxes.Any())
        {
            yield break;
        }

        var overflowAmounts = overflowBoxes.SelectMany(b => b.Cards);

        foreach (var source in overflowAmounts)
        {
            foreach (var assignment in OverflowAssignment(source))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<BoxAssignment<Amount>> OverflowAssignment(Amount source)
    {
        _exactMatches ??= AddLookup();

        var bestBoxes = _exactMatches[source.CardId];

        if (!bestBoxes.Any())
        {
            return Enumerable.Empty<BoxAssignment<Amount>>();
        }

        if (source.Location is not Box sourceBox)
        {
            return Enumerable.Empty<BoxAssignment<Amount>>();
        }

        var boxSpace = _treasuryContext.BoxSpace;

        int copiesAbove = boxSpace.GetValueOrDefault(sourceBox) - sourceBox.Capacity;
        if (copiesAbove <= 0)
        {
            return Enumerable.Empty<BoxAssignment<Amount>>();
        }

        int minTransfer = Math.Min(source.NumCopies, copiesAbove);

        return Assignment.FitToBoxes(source, minTransfer, bestBoxes, boxSpace);
    }


    private ILookup<string, Box> AddLookup()
    {
        var (available, overflowBoxes, _, boxSpace) = _treasuryContext;

        var availableCards = available.SelectMany(b => b.Cards);

        var overflowCards = overflowBoxes
            .SelectMany(b => b.Cards)
            .Select(a => a.Card);

        return Assignment.ExactAddLookup(availableCards, overflowCards, boxSpace);
    }
}


internal class ApproximateOverflow : OverflowHandler
{
    private ILookup<string, Box>? _approxMatches;

    public ApproximateOverflow(TreasuryContext treasuryContext)
        : base(treasuryContext)
    { }


    protected override IEnumerable<BoxAssignment<Amount>> GetAssignments()
    {
        var overflowBoxes = _treasuryContext.Overflow;

        if (!overflowBoxes.Any())
        {
            yield break;
        }

        var overflowAmounts = overflowBoxes.SelectMany(b => b.Cards);

        foreach (var source in overflowAmounts)
        {
            foreach (var assignment in OverflowAssignment(source))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<BoxAssignment<Amount>> OverflowAssignment(Amount source)
    {
        _approxMatches ??= AddLookup();

        if (source.Location is not Box sourceBox)
        {
            return Enumerable.Empty<BoxAssignment<Amount>>();
        }

        var boxSpace = _treasuryContext.BoxSpace;

        int copiesAbove = boxSpace.GetValueOrDefault(sourceBox) - sourceBox.Capacity;
        if (copiesAbove <= 0)
        {
            return Enumerable.Empty<BoxAssignment<Amount>>();
        }

        var bestBoxes = _approxMatches[source.Card.Name]
            .Union(_treasuryContext.Available)
            .Concat(_treasuryContext.Excess);

        int minTransfer = Math.Min(source.NumCopies, copiesAbove);

        return Assignment.FitToBoxes(source, minTransfer, bestBoxes, boxSpace);
    }


    private ILookup<string, Box> AddLookup()
    {
        var (available, overflowBoxes, _, boxSpace) = _treasuryContext;

        var availableCards = available.SelectMany(b => b.Cards);

        var overflowCards = overflowBoxes
            .SelectMany(b => b.Cards)
            .Select(a => a.Card);

        return Assignment.ApproxAddLookup(availableCards, overflowCards, boxSpace);
    }
}
