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

    protected abstract IEnumerable<StorageAssignment<Amount>> GetAssignments();

    public void TransferOverflow()
    {
        foreach ((Amount source, int numCopies, Storage storage) in GetAssignments())
        {
            TreasuryContext.TransferCopies(source.Card, numCopies, storage, source.Location);
        }
    }
}


internal class ExactOverflow: OverflowHandler
{
    private ILookup<string, Storage>? _exactMatches;

    public ExactOverflow(TreasuryContext treasuryContext)
        : base(treasuryContext)
    { }


    protected override IEnumerable<StorageAssignment<Amount>> GetAssignments()
    {
        var (available, overflowBoxes, _, _) = TreasuryContext;

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


    private IEnumerable<StorageAssignment<Amount>> OverflowAssignment(Amount source)
    {
        _exactMatches ??= AddLookup();

        var bestBoxes = _exactMatches[source.CardId];

        if (!bestBoxes.Any())
        {
            return Enumerable.Empty<StorageAssignment<Amount>>();
        }

        if (source.Location is not Box sourceBox)
        {
            return Enumerable.Empty<StorageAssignment<Amount>>();
        }

        var storageSpace = TreasuryContext.StorageSpace;

        int copiesAbove = storageSpace.GetValueOrDefault(sourceBox) - sourceBox.Capacity;
        if (copiesAbove <= 0)
        {
            return Enumerable.Empty<StorageAssignment<Amount>>();
        }

        int minTransfer = Math.Min(source.Copies, copiesAbove);

        return Assignment.FitToBoxes(source, minTransfer, bestBoxes, storageSpace);
    }


    private ILookup<string, Storage> AddLookup()
    {
        var (available, overflowBoxes, _, storageSpace) = TreasuryContext;

        var availableCards = available.SelectMany(b => b.Cards);

        var overflowCards = overflowBoxes
            .SelectMany(b => b.Cards)
            .Select(a => a.Card);

        return Assignment.ExactAddLookup(availableCards, overflowCards, storageSpace);
    }
}


internal class ApproximateOverflow : OverflowHandler
{
    private ILookup<string, Storage>? _approxMatches;

    public ApproximateOverflow(TreasuryContext treasuryContext)
        : base(treasuryContext)
    { }


    protected override IEnumerable<StorageAssignment<Amount>> GetAssignments()
    {
        var overflowBoxes = TreasuryContext.Overflow;

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


    private IEnumerable<StorageAssignment<Amount>> OverflowAssignment(Amount source)
    {
        _approxMatches ??= AddLookup();

        if (source.Location is not Box sourceBox)
        {
            return Enumerable.Empty<StorageAssignment<Amount>>();
        }

        var (available, _, excessStorage, storageSpace) = TreasuryContext;

        int copiesAbove = storageSpace.GetValueOrDefault(sourceBox) - sourceBox.Capacity;
        if (copiesAbove <= 0)
        {
            return Enumerable.Empty<StorageAssignment<Amount>>();
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

        var availableCards = available.SelectMany(b => b.Cards);

        var overflowCards = overflowBoxes
            .SelectMany(b => b.Cards)
            .Select(a => a.Card);

        return Assignment.ApproxAddLookup(availableCards, overflowCards, storageSpace);
    }
}
