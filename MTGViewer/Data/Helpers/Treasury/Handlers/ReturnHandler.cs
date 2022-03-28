using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Internal;

internal static class ReturnExtensions
{
    public static void ReturnExact(this ExchangeContext exchangeContext)
    {
        new ExactReturn(exchangeContext).ApplyReturns();
    }

    public static void ReturnApproximate(this ExchangeContext exchangeContext)
    {
        new ApproximateReturn(exchangeContext).ApplyReturns();
    }

    public static void ReturnGuess(this ExchangeContext exchangeContext)
    {
        new GuessReturn(exchangeContext).ApplyReturns();
    }
}


internal abstract class ReturnHandler
{
    protected ExchangeContext ExchangeContext { get; }
    protected TreasuryContext TreasuryContext => ExchangeContext.TreasuryContext;

    protected ReturnHandler(ExchangeContext exchangeContext)
    {
        ExchangeContext = exchangeContext;
    }

    protected abstract IEnumerable<StorageAssignment<Card>> GetAssignments();

    public void ApplyReturns()
    {
        foreach ((Card card, int copies, Storage storage) in GetAssignments())
        {
            ExchangeContext.ReturnCopies(card, copies, storage);
        }
    }
}


internal class ExactReturn : ReturnHandler
{
    private ILookup<string, Storage>? _exactMatch;

    public ExactReturn(ExchangeContext exchangeContext) : base(exchangeContext)
    { }


    protected override IEnumerable<StorageAssignment<Card>> GetAssignments()
    {
        var giveBacks = ExchangeContext.Deck.GiveBacks;

        if (!TreasuryContext.Available.Any() || giveBacks.All(g => g.Copies == 0))
        {
            yield break;
        }

        foreach (var giveBack in giveBacks)
        {
            foreach (var assignment in FitToStorage(giveBack))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<Card>> FitToStorage(GiveBack giveBack)
    {
        _exactMatch ??= AddLookup();

        var matches = _exactMatch[giveBack.CardId];
        var storageSpaces = TreasuryContext.StorageSpaces;

        return Assignment.FitToStorage(
            giveBack.Card, giveBack.Copies, matches, storageSpaces);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var availableHolds = TreasuryContext.Available.SelectMany(b => b.Holds);

        var giveCards = ExchangeContext.Deck.GiveBacks.Select(w => w.Card);

        // TODO: account for changing Copies while iter
        return Assignment.ExactAddLookup(availableHolds, giveCards);
    }
}


internal class ApproximateReturn : ReturnHandler
{
    private ILookup<string, Storage>? _approxMatch;

    public ApproximateReturn(ExchangeContext exchangeContext) : base(exchangeContext)
    { }


    protected override IEnumerable<StorageAssignment<Card>> GetAssignments()
    {
        var giveBacks = ExchangeContext.Deck.GiveBacks;

        if (!TreasuryContext.Available.Any() || giveBacks.All(g => g.Copies == 0))
        {
            yield break;
        }

        foreach (var giveBack in giveBacks)
        {
            if (giveBack.Copies == 0)
            {
                continue;
            }

            foreach (var assignment in FitToStorage(giveBack))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<Card>> FitToStorage(GiveBack giveBack)
    {
        _approxMatch ??= AddLookup();

        var matches = _approxMatch[giveBack.Card.Name];
        var storageSpaces = TreasuryContext.StorageSpaces;

        return Assignment.FitToStorage(
            giveBack.Card, giveBack.Copies, matches, storageSpaces);
    }


    private ILookup<string, Storage> AddLookup()
    {
        var availableHolds = TreasuryContext.Available.SelectMany(b => b.Holds);
        var giveCards = ExchangeContext.Deck.GiveBacks.Select(w => w.Card);

        // TODO: account for changing Copies while iter
        return Assignment.ApproxAddLookup(availableHolds, giveCards);
    }
}


internal class GuessReturn : ReturnHandler
{
    private BoxSearcher? _boxSearch;

    public GuessReturn(ExchangeContext exchangeContext) : base(exchangeContext)
    { }


    protected override IEnumerable<StorageAssignment<Card>> GetAssignments()
    {
        var giveBacks = ExchangeContext.Deck.GiveBacks;

        if (giveBacks.All(g => g.Copies == 0))
        {
            yield break;
        }

        // descending so that the first added cards do not shift down the 
        // positioning of the sorted card holds
        // each of the returned cards should have less effect on following returns
        // keep eye on

        var orderedGiveBacks = giveBacks
            .OrderByDescending(g => g.Copies)
                .ThenByDescending(g => g.Card.Name)
                .ThenByDescending(g => g.Card.SetName);

        foreach (var giveBack in orderedGiveBacks)
        {
            if (giveBack.Copies == 0)
            {
                continue;
            }

            foreach (var assignment in FitToStorage(giveBack))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<Card>> FitToStorage(GiveBack giveBack)
    {
        var (available, _, excess, storageSpaces) = TreasuryContext;

        _boxSearch ??= new BoxSearcher(available);

        var matches = _boxSearch
            .FindBestBoxes(giveBack.Card)
            .Union(available)
            .Cast<Storage>()
            .Concat(excess);

        return Assignment.FitToStorage(giveBack.Card, giveBack.Copies, matches, storageSpaces);
    }
}
