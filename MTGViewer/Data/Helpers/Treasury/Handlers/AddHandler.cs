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

    protected abstract IEnumerable<StorageAssignment<CardRequest>> GetAssignments();

    public void AddCopies()
    {
        foreach ((CardRequest request, int copies, Storage storage) in GetAssignments())
        {
            TreasuryContext.AddCopies(request.Card, copies, storage);
            request.Copies -= copies;
        }
    }
}


internal class ExactAdd : AddHandler
{
    private ILookup<string, Storage>? _exactMatches;

    public ExactAdd(TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
        : base(treasuryContext, requests)
    { }


    protected override IEnumerable<StorageAssignment<CardRequest>> GetAssignments()
    {
        if (CardRequests.All(cr => cr.Copies == 0))
        {
            yield break;
        }

        foreach (CardRequest request in CardRequests)
        {
            if (request.Copies == 0)
            {
                continue;
            }

            foreach (var assignment in FitToStorage(request))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<CardRequest>> FitToStorage(CardRequest request)
    {
        _exactMatches ??= AddLookup();

        var (card, copies) = request;

        var matches = _exactMatches[card.Id];
        var storageSpaces = TreasuryContext.StorageSpaces;

        return Assignment.FitToStorage(request, copies, matches, storageSpaces);
    }


    private ILookup<string, Storage> AddLookup()
    {
        var (available, _, _, storageSpaces) = TreasuryContext;

        var availableCards = available.SelectMany(b => b.Holds);
        var cardRequests = CardRequests.Select(cr => cr.Card);

        return Assignment.ExactAddLookup(availableCards, cardRequests, storageSpaces);
    }
}


internal class ApproximateAdd : AddHandler
{
    private ILookup<string, Storage>? _approxMatches;

    public ApproximateAdd(TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
        : base(treasuryContext, requests)
    { }


    protected override IEnumerable<StorageAssignment<CardRequest>> GetAssignments()
    {
        if (CardRequests.All(cr => cr.Copies == 0))
        {
            yield break;
        }

        foreach (CardRequest request in CardRequests)
        {
            if (request.Copies == 0)
            {
                continue;
            }

            foreach (var assignment in FitToStorage(request))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<CardRequest>> FitToStorage(CardRequest request)
    {
        _approxMatches ??= AddLookup();

        var (card, copies) = request;

        var matches = _approxMatches[card.Name];
        var storageSpaces = TreasuryContext.StorageSpaces;

        return Assignment.FitToStorage(request, copies, matches, storageSpaces);
    }


    private ILookup<string, Storage> AddLookup()
    {
        var (available, _, _, storageSpace) = TreasuryContext;

        var availableCards = available.SelectMany(b => b.Holds);
        var cardRequests = CardRequests.Select(cr => cr.Card);

        return Assignment.ApproxAddLookup(availableCards, cardRequests, storageSpace);
    }
}


internal class GuessAdd : AddHandler
{
    private BoxSearcher? _boxSearch;

    public GuessAdd(TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
        : base(treasuryContext, requests)
    { }


    protected override IEnumerable<StorageAssignment<CardRequest>> GetAssignments()
    {
        if (CardRequests.All(cr => cr.Copies == 0))
        {
            yield break;
        }

        // descending so that the first added cards do not shift down the 
        // positioning of the sorted card holds
        // each of the returned cards should have less effect on following returns
        // keep eye on

        var orderedRequests = CardRequests
            .OrderByDescending(cr => cr.Copies)
                .ThenByDescending(cr => cr.Card.Name)
                .ThenByDescending(cr => cr.Card.SetName);

        foreach (CardRequest request in orderedRequests)
        {
            if (request.Copies == 0)
            {
                continue;
            }

            foreach (var assignment in FitToStorage(request))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<CardRequest>> FitToStorage(CardRequest request)
    {
        var (card, copies) = request;
        var (available, _, excess, storageSpaces) = TreasuryContext;

        _boxSearch ??= new BoxSearcher(available);

        var matches = _boxSearch
            .FindBestBoxes(card)
            .Union(available)
            .Cast<Storage>()
            .Concat(excess);

        return Assignment.FitToStorage(request, copies, matches, storageSpaces);
    }
}