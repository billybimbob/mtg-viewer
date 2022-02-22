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
    protected readonly ExchangeContext _exchangeContext;
    protected readonly TreasuryContext _treasuryContext;

    public ReturnHandler(ExchangeContext exchangeContext)
    {
        _exchangeContext = exchangeContext;
        _treasuryContext = exchangeContext.TreasuryContext;
    }

    protected abstract IEnumerable<BoxAssignment<Card>> GetAssignments();

    public void ApplyReturns()
    {
        foreach ((Card card, int numCopies, Box box) in GetAssignments())
        {
            _exchangeContext.ReturnCopies(card, numCopies, box);
        }
    }
}


internal class ExactReturn : ReturnHandler
{
    private ILookup<string, Box>? _exactMatch;

    public ExactReturn(ExchangeContext exchangeContext) : base(exchangeContext)
    { }


    protected override IEnumerable<BoxAssignment<Card>> GetAssignments()
    {
        var giveBacks = _exchangeContext.Deck.GiveBacks;

        if (!_treasuryContext.Available.Any() || giveBacks.All(g => g.NumCopies == 0))
        {
            yield break;
        }

        foreach (var giveBack in giveBacks)
        {
            foreach (var assignment in FitToBoxes(giveBack))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<BoxAssignment<Card>> FitToBoxes(GiveBack giveBack)
    {
        _exactMatch ??= AddLookup();

        var bestBoxes = _exactMatch[giveBack.CardId];

        return Assignment.FitToBoxes(
            giveBack.Card, giveBack.NumCopies, bestBoxes, _treasuryContext.BoxSpace);
    }

    private ILookup<string, Box> AddLookup()
    {
        var (available, _, _, boxSpace) = _treasuryContext;
        var availableAmounts = available.SelectMany(b => b.Cards);

        var giveCards = _exchangeContext.Deck.GiveBacks.Select(w => w.Card);

        // TODO: account for changing NumCopies while iter
        return Assignment.ExactAddLookup(availableAmounts, giveCards, boxSpace);
    }
}


internal class ApproximateReturn : ReturnHandler
{
    private ILookup<string, Box>? _approxMatch;

    public ApproximateReturn(ExchangeContext exchangeContext) : base(exchangeContext)
    { }


    protected override IEnumerable<BoxAssignment<Card>> GetAssignments()
    {
        var giveBacks = _exchangeContext.Deck.GiveBacks;

        if (!_treasuryContext.Available.Any() || giveBacks.All(g => g.NumCopies == 0))
        {
            yield break;
        }

        foreach (var giveBack in giveBacks)
        {
            if (giveBack.NumCopies == 0)
            {
                continue;
            }

            foreach (var assignment in FitToBoxes(giveBack))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<BoxAssignment<Card>> FitToBoxes(GiveBack giveBack)
    {
        _approxMatch ??= AddLookup();

        var bestBoxes = _approxMatch[giveBack.Card.Name];

        return Assignment.FitToBoxes(
            giveBack.Card, giveBack.NumCopies, bestBoxes, _treasuryContext.BoxSpace);
    }

    private ILookup<string, Box> AddLookup()
    {
        var availableAmounts = _treasuryContext.Available.SelectMany(b => b.Cards);
        var giveCards = _exchangeContext.Deck.GiveBacks.Select(w => w.Card);

        var boxSpace = _treasuryContext.BoxSpace;

        // TODO: account for changing NumCopies while iter
        return Assignment.ApproxAddLookup(availableAmounts, giveCards, boxSpace);
    }
}


internal class GuessReturn : ReturnHandler
{
    private BoxSearcher? _boxSearch;

    public GuessReturn(ExchangeContext exchangeContext) : base(exchangeContext)
    { }


    protected override IEnumerable<BoxAssignment<Card>> GetAssignments()
    {
        var giveBacks = _exchangeContext.Deck.GiveBacks;

        if (giveBacks.All(g => g.NumCopies == 0))
        {
            yield break;
        }

        // descending so that the first added cards do not shift down the 
        // positioning of the sorted card amounts
        // each of the returned cards should have less effect on following returns
        // keep eye on

        var orderedGiveBacks = giveBacks
            .OrderByDescending(g => g.NumCopies)
                .ThenByDescending(g => g.Card.Name)
                    .ThenByDescending(g => g.Card.SetName);

        foreach (var giveBack in orderedGiveBacks)
        {
            if (giveBack.NumCopies == 0)
            {
                continue;
            }

            foreach (var assignment in FitToBoxes(giveBack))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<BoxAssignment<Card>> FitToBoxes(GiveBack giveBack)
    {
        var (available, excess, _, boxSpace) = _treasuryContext;

        _boxSearch ??= new BoxSearcher(available);

        var bestBoxes = _boxSearch
            .FindBestBoxes(giveBack.Card)
            .Union(available)
            .Concat(excess);
        
        return Assignment.FitToBoxes(
            giveBack.Card, giveBack.NumCopies, bestBoxes, boxSpace);
    }
}
