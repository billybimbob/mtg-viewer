using System.Linq;
using System.Collections.Generic;

namespace MTGViewer.Data.Treasury;

internal static class ExcessExtensions
{
    public static void LowerExactExcess(this TreasuryContext treasuryContext)
        => new ExactExcess(treasuryContext)
            .TransferExcess();

    public static void LowerApproximateExcess(this TreasuryContext treasuryContext)
        => new ApproximateExcess(treasuryContext)
            .TransferExcess();
}

internal abstract class ExcessHandler
{
    protected TreasuryContext TreasuryContext { get; }

    protected ExcessHandler(TreasuryContext treasuryContext)
    {
        TreasuryContext = treasuryContext;
    }

    protected abstract IEnumerable<Assignment<Hold>> GetAssignments();

    public void TransferExcess()
    {
        foreach ((var source, int copies, var target) in GetAssignments())
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

    protected override IEnumerable<Assignment<Hold>> GetAssignments()
    {
        var (available, _, excessStorage, _) = TreasuryContext;
        var excessHolds = excessStorage.SelectMany(b => b.Holds);

        if (!available.Any() || excessHolds.All(h => h.Copies == 0))
        {
            yield break;
        }

        foreach (var excess in excessHolds)
        {
            foreach (var assignment in FitToStorage(excess))
            {
                yield return assignment;
            }
        }
    }

    private IEnumerable<Assignment<Hold>> FitToStorage(Hold excess)
    {
        _exactMatches ??= AddLookup();

        var matches = _exactMatches[excess.CardId];
        var storageSpaces = TreasuryContext.StorageSpaces;

        return Assigner.FitToStorage(excess, excess.Copies, matches, storageSpaces);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var (available, _, excess, _) = TreasuryContext;

        var availableHolds = available.SelectMany(b => b.Holds);

        var excessCards = excess
            .SelectMany(b => b.Holds)
            .Select(h => h.Card);

        // TODO: account for changing Copies while iter
        return Assigner.ExactAddLookup(availableHolds, excessCards);
    }
}

internal class ApproximateExcess : ExcessHandler
{
    private ILookup<string, Storage>? _approxMatches;
    private BoxSearcher? _boxSearch;

    public ApproximateExcess(TreasuryContext treasuryContext) : base(treasuryContext)
    { }

    protected override IEnumerable<Assignment<Hold>> GetAssignments()
    {
        var (available, _, excessStorage, _) = TreasuryContext;
        var excessHolds = excessStorage.SelectMany(b => b.Holds);

        if (!available.Any() || excessHolds.All(h => h.Copies == 0))
        {
            yield break;
        }

        foreach (var excess in excessHolds)
        {
            foreach (var assignment in FitToStorage(excess))
            {
                yield return assignment;
            }
        }
    }

    private IEnumerable<Assignment<Hold>> FitToStorage(Hold excess)
    {
        var (available, _, _, storageSpaces) = TreasuryContext;

        _approxMatches ??= AddLookup();
        _boxSearch ??= new BoxSearcher(available);

        var matches = _approxMatches[excess.Card.Name]
            .Union(_boxSearch.FindBestBoxes(excess.Card))
            .Union(available);

        return Assigner.FitToStorage(excess, excess.Copies, matches, storageSpaces);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var (available, _, excess, _) = TreasuryContext;

        var availableHolds = available.SelectMany(b => b.Holds);

        var excessCards = excess
            .SelectMany(b => b.Holds)
            .Select(h => h.Card);

        // TODO: account for changing Copies while iter
        return Assigner.ApproxAddLookup(availableHolds, excessCards);
    }
}
