using System;
using System.Collections.Generic;
using System.Linq;

namespace MtgViewer.Data.Treasury.Handlers;

internal abstract class OverflowHandler
{
    protected TreasuryContext TreasuryContext { get; }

    protected OverflowHandler(TreasuryContext treasuryContext)
    {
        TreasuryContext = treasuryContext;
    }

    protected abstract IEnumerable<Assignment<Hold>> GetAssignments();

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
    {
    }

    protected override IEnumerable<Assignment<Hold>> GetAssignments()
    {
        var (available, overflowBoxes, _, _) = TreasuryContext;

        var overflow = overflowBoxes.SelectMany(b => b.Holds);

        if (!available.Any() || !overflow.Any(h => h.Copies > 0))
        {
            yield break;
        }

        foreach (var source in overflow)
        {
            foreach (var assignment in OverflowAssignment(source))
            {
                yield return assignment;
            }
        }
    }

    private IEnumerable<Assignment<Hold>> OverflowAssignment(Hold source)
    {
        var storageSpaces = TreasuryContext.StorageSpaces;

        var index = (LocationIndex)source.Location;

        if (storageSpaces.GetValueOrDefault(index)
            is not { Remaining: < 0 and int deficit })
        {
            return Enumerable.Empty<Assignment<Hold>>();
        }

        _exactMatches ??= AddLookup();

        var matches = _exactMatches[source.CardId];

        if (!matches.Any())
        {
            return Enumerable.Empty<Assignment<Hold>>();
        }

        int minTransfer = Math.Min(source.Copies, Math.Abs(deficit));

        return Assigner.FitToStorage(source, minTransfer, matches, storageSpaces);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var (available, overflowBoxes, _, _) = TreasuryContext;

        var targets = available.SelectMany(b => b.Holds);

        var overflowCards = overflowBoxes
            .SelectMany(b => b.Holds)
            .Select(h => h.Card);

        return Assigner.ExactAddLookup(targets, overflowCards);
    }
}

internal class ApproximateOverflow : OverflowHandler
{
    private ILookup<string, Storage>? _approxMatches;
    private BoxSearcher? _boxSearch;

    public ApproximateOverflow(TreasuryContext treasuryContext)
        : base(treasuryContext)
    {
    }

    protected override IEnumerable<Assignment<Hold>> GetAssignments()
    {
        var overflow = TreasuryContext.Overflow.SelectMany(b => b.Holds);

        if (!overflow.Any(h => h.Copies > 0))
        {
            yield break;
        }

        foreach (var source in overflow)
        {
            foreach (var assignment in OverflowAssignment(source))
            {
                yield return assignment;
            }
        }
    }

    private IEnumerable<Assignment<Hold>> OverflowAssignment(Hold source)
    {
        var (available, _, excess, storageSpaces) = TreasuryContext;

        var index = (LocationIndex)source.Location;

        if (storageSpaces.GetValueOrDefault(index)
            is not { Remaining: < 0 and int deficit })
        {
            return Enumerable.Empty<Assignment<Hold>>();
        }

        _approxMatches ??= AddLookup();
        _boxSearch ??= new BoxSearcher(available);

        var matches = _approxMatches[source.Card.Name]
            .Union(_boxSearch.FindBestBoxes(source.Card))
            .Union(available)
            .Concat(excess);

        if (!matches.Any())
        {
            return Enumerable.Empty<Assignment<Hold>>();
        }

        int minTransfer = Math.Min(source.Copies, Math.Abs(deficit));

        return Assigner.FitToStorage(source, minTransfer, matches, storageSpaces);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var (available, overflow, _, _) = TreasuryContext;

        var targets = available.SelectMany(b => b.Holds);

        var overflowCards = overflow
            .SelectMany(b => b.Holds)
            .Select(h => h.Card);

        return Assigner.ApproxAddLookup(targets, overflowCards);
    }
}
