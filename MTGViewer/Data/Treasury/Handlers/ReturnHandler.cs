using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Treasury.Handlers;

internal abstract class ReturnHandler
{
    protected ExchangeContext ExchangeContext { get; }
    protected TreasuryContext TreasuryContext => ExchangeContext.TreasuryContext;

    protected ReturnHandler(ExchangeContext exchangeContext)
    {
        ExchangeContext = exchangeContext;
    }

    protected abstract IEnumerable<Assignment<Card>> GetAssignments();

    public void ApplyReturns()
    {
        foreach ((var card, int copies, var storage) in GetAssignments())
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

    protected override IEnumerable<Assignment<Card>> GetAssignments()
    {
        var givebacks = ExchangeContext.Deck.Givebacks;

        if (!TreasuryContext.Available.Any() || givebacks.All(g => g.Copies == 0))
        {
            yield break;
        }

        foreach (var giveback in givebacks)
        {
            foreach (var assignment in FitToStorage(giveback))
            {
                yield return assignment;
            }
        }
    }

    private IEnumerable<Assignment<Card>> FitToStorage(Giveback giveback)
    {
        _exactMatch ??= AddLookup();

        var matches = _exactMatch[giveback.CardId];
        var storageSpaces = TreasuryContext.StorageSpaces;

        return Assigner.FitToStorage(
            giveback.Card, giveback.Copies, matches, storageSpaces);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var availableHolds = TreasuryContext.Available.SelectMany(b => b.Holds);

        var giveCards = ExchangeContext.Deck.Givebacks.Select(w => w.Card);

        // TODO: account for changing Copies while iter
        return Assigner.ExactAddLookup(availableHolds, giveCards);
    }
}

internal class ApproximateReturn : ReturnHandler
{
    private ILookup<string, Storage>? _approxMatch;

    public ApproximateReturn(ExchangeContext exchangeContext) : base(exchangeContext)
    { }

    protected override IEnumerable<Assignment<Card>> GetAssignments()
    {
        var givebacks = ExchangeContext.Deck.Givebacks;

        if (!TreasuryContext.Available.Any() || givebacks.All(g => g.Copies == 0))
        {
            yield break;
        }

        foreach (var giveback in givebacks)
        {
            if (giveback.Copies == 0)
            {
                continue;
            }

            foreach (var assignment in FitToStorage(giveback))
            {
                yield return assignment;
            }
        }
    }

    private IEnumerable<Assignment<Card>> FitToStorage(Giveback giveback)
    {
        _approxMatch ??= AddLookup();

        var matches = _approxMatch[giveback.Card.Name];
        var storageSpaces = TreasuryContext.StorageSpaces;

        return Assigner.FitToStorage(
            giveback.Card, giveback.Copies, matches, storageSpaces);
    }

    private ILookup<string, Storage> AddLookup()
    {
        var availableHolds = TreasuryContext.Available.SelectMany(b => b.Holds);
        var giveCards = ExchangeContext.Deck.Givebacks.Select(w => w.Card);

        // TODO: account for changing Copies while iter
        return Assigner.ApproxAddLookup(availableHolds, giveCards);
    }
}

internal class GuessReturn : ReturnHandler
{
    private BoxSearcher? _boxSearch;

    public GuessReturn(ExchangeContext exchangeContext) : base(exchangeContext)
    { }

    protected override IEnumerable<Assignment<Card>> GetAssignments()
    {
        var givebacks = ExchangeContext.Deck.Givebacks;

        if (givebacks.All(g => g.Copies == 0))
        {
            yield break;
        }

        // descending so that the first added cards do not shift down the
        // positioning of the sorted card holds
        // each of the returned cards should have less effect on following returns
        // keep eye on

        var orderedGivebacks = BoxSearcher.GetOrderedRequests(givebacks, g => g.Card);

        foreach (var giveback in orderedGivebacks)
        {
            if (giveback.Copies == 0)
            {
                continue;
            }

            foreach (var assignment in FitToStorage(giveback))
            {
                yield return assignment;
            }
        }
    }

    private IEnumerable<Assignment<Card>> FitToStorage(Giveback giveback)
    {
        var (available, _, excess, storageSpaces) = TreasuryContext;

        _boxSearch ??= new BoxSearcher(available);

        var matches = _boxSearch
            .FindBestBoxes(giveback.Card)
            .Union(available)
            .Cast<Storage>()
            .Concat(excess);

        return Assigner.FitToStorage(giveback.Card, giveback.Copies, matches, storageSpaces);
    }
}
