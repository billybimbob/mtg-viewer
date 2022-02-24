using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Internal;

internal static class AddExtensions
{
    public static void AddExact(this TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
    {
        new ExactAdd(treasuryContext, requests).AddCopies();
    }

    public static void AddApproximate(this TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
    {
        new ApproximateAdd(treasuryContext, requests).AddCopies();
    }

    public static void AddGuess(this TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
    {
        new GuessAdd(treasuryContext, requests).AddCopies();
    }
}


internal abstract class AddHandler
{
    protected TreasuryContext TreasuryContext { get; }
    protected IEnumerable<CardRequest> CardRequests { get; }

    protected AddHandler(TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
    {
        TreasuryContext = treasuryContext;
        CardRequests = requests;
    }

    protected abstract IEnumerable<BoxAssignment<CardRequest>> GetAssignments();

    public void AddCopies()
    {
        foreach ((CardRequest request, int numCopies, Box box) in GetAssignments())
        {
            TreasuryContext.AddCopies(request.Card, numCopies, box);
            request.NumCopies -= numCopies;
        }
    }
}


internal class ExactAdd : AddHandler
{
    private ILookup<string, Box>? _exactMatches;

    public ExactAdd(TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
        : base(treasuryContext, requests)
    { }


    protected override IEnumerable<BoxAssignment<CardRequest>> GetAssignments()
    {
        if (CardRequests.All(cr => cr.NumCopies == 0))
        {
            yield break;
        }

        foreach (CardRequest request in CardRequests)
        {
            if (request.NumCopies == 0)
            {
                continue;
            }

            foreach (var assignment in FitToBoxes(request))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<BoxAssignment<CardRequest>> FitToBoxes(CardRequest request)
    {
        _exactMatches ??= AddLookup();

        var (card, numCopies) = request;

        var possibleBoxes = _exactMatches[card.Id];
        var boxSpace = TreasuryContext.BoxSpace;

        return Assignment.FitToBoxes(request, numCopies, possibleBoxes, boxSpace);
    }


    private ILookup<string, Box> AddLookup()
    {
        var (available, _, _, boxSpace) = TreasuryContext;

        var availableCards = available.SelectMany(b => b.Cards);
        var cardRequests = CardRequests.Select(cr => cr.Card);

        return Assignment.ExactAddLookup(availableCards, cardRequests, boxSpace);
    }
}


internal class ApproximateAdd : AddHandler
{
    private ILookup<string, Box>? _approxMatches;

    public ApproximateAdd(TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
        : base(treasuryContext, requests)
    { }


    protected override IEnumerable<BoxAssignment<CardRequest>> GetAssignments()
    {
        if (CardRequests.All(cr => cr.NumCopies == 0))
        {
            yield break;
        }

        foreach (CardRequest request in CardRequests)
        {
            if (request.NumCopies == 0)
            {
                continue;
            }

            foreach (var assignment in FitToBoxes(request))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<BoxAssignment<CardRequest>> FitToBoxes(CardRequest request)
    {
        _approxMatches ??= AddLookup();

        var (card, numCopies) = request;

        var possibleBoxes = _approxMatches[card.Name];
        var boxSpace = TreasuryContext.BoxSpace;

        return Assignment.FitToBoxes(request, numCopies, possibleBoxes, boxSpace);
    }


    private ILookup<string, Box> AddLookup()
    {
        var (available, _, _, boxSpace) = TreasuryContext;

        var availableCards = available.SelectMany(b => b.Cards);
        var cardRequests = CardRequests.Select(cr => cr.Card);

        return Assignment.ApproxAddLookup(availableCards, cardRequests, boxSpace);
    }
}


internal class GuessAdd : AddHandler
{
    private BoxSearcher? _boxSearch;

    public GuessAdd(TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
        : base(treasuryContext, requests)
    { }


    protected override IEnumerable<BoxAssignment<CardRequest>> GetAssignments()
    {
        if (CardRequests.All(cr => cr.NumCopies == 0))
        {
            yield break;
        }

        // descending so that the first added cards do not shift down the 
        // positioning of the sorted card amounts
        // each of the returned cards should have less effect on following returns
        // keep eye on

        var orderedRequests = CardRequests
            .OrderByDescending(cr => cr.NumCopies)
                .ThenByDescending(cr => cr.Card.Name)
                    .ThenByDescending(cr => cr.Card.SetName);

        foreach (CardRequest request in orderedRequests)
        {
            if (request.NumCopies == 0)
            {
                continue;
            }

            foreach (var assignment in FitToBoxes(request))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<BoxAssignment<CardRequest>> FitToBoxes(CardRequest request)
    {
        var (card, numCopies) = request;
        var (available, _, excess, boxSpace) = TreasuryContext;

        _boxSearch ??= new BoxSearcher(available);

        var bestBoxes = _boxSearch
            .FindBestBoxes(card)
            .Union(available)
            .Concat(excess);

        return Assignment.FitToBoxes(request, numCopies, bestBoxes, boxSpace);
    }
}