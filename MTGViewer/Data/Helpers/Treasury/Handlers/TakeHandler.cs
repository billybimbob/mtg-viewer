using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Internal;

internal static class TakeExtensions
{
    public static void TakeExact(this ExchangeContext exchangeContext)
    {
        new ExactTake(exchangeContext).ApplyTakes();
    }

    public static void TakeApproximate(this ExchangeContext exchangeContext)
    {
        new ApproximateTake(exchangeContext).ApplyTakes();
    }
}


internal abstract class TakeHandler
{
    protected ExchangeContext ExchangeContext { get; }
    protected TreasuryContext TreasuryContext => ExchangeContext.TreasuryContext;

    protected TakeHandler(ExchangeContext exchangeContext)
    {
        ExchangeContext = exchangeContext;
    }

    protected abstract IEnumerable<StorageAssignment<Card>> GetAssignments();

    public void ApplyTakes()
    {
        foreach ((Card card, int numCopies, Storage target) in GetAssignments())
        {
            ExchangeContext.TakeCopies(card, numCopies, target);
        }
    }

    protected static IEnumerable<StorageAssignment<TSource>> TakeFromStorage<TSource>(
        TSource source,
        int cardsToTake,
        IEnumerable<Hold> boxHolds)
    {
        foreach (var hold in boxHolds)
        {
            if (hold.Location is not Box box)
            {
                continue;
            }

            int takeCopies = Math.Min(cardsToTake, hold.Copies);
            if (takeCopies == 0)
            {
                continue;
            }

            yield return new StorageAssignment<TSource>(source, takeCopies, box);

            cardsToTake -= takeCopies;
            if (cardsToTake == 0)
            {
                yield break;
            }
        }
    }
}


internal class ExactTake : TakeHandler
{
    private ILookup<string, Hold>? _exactTake;

    public ExactTake(ExchangeContext exchangeContext) : base(exchangeContext)
    { }


    protected override IEnumerable<StorageAssignment<Card>> GetAssignments()
    {
        var wants = ExchangeContext.Deck.Wants;

        if (wants.All(w => w.Copies == 0))
        {
            yield break;
        }


        foreach (var want in wants)
        {
            foreach (var assignment in TakeFromStorage(want))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<Card>> TakeFromStorage(Want want)
    {
        _exactTake ??= TakeLookup();

        var idPositions = _exactTake[want.CardId];

        return TakeFromStorage(want.Card, want.Copies, idPositions);
    }

    // take assignments should take from smaller dup stacks first
    // in boxes with less available space

    private ILookup<string, Hold> TakeLookup()
    {
        var targets = TreasuryContext.Holds;

        var cardIds = ExchangeContext.Deck.Wants
            .Select(w => w.CardId)
            .Distinct();

        var storageSpace = TreasuryContext.StorageSpace;

        // TODO: account for changing NumCopies while iter
        return targets
            .Join( cardIds,
                h => h.CardId, cid => cid,
                (target, _) => target)

            .OrderBy(h => h.Copies)
                .ThenBy(h => h.Location switch
                {
                    Box box => box.Capacity - storageSpace.GetValueOrDefault(box),
                    Excess excess => -storageSpace.GetValueOrDefault(excess),
                    _ => throw new ArgumentException(nameof(targets))
                })
            
            // lookup group orders should preserve NumCopies order
            .ToLookup(h => h.CardId);
    }
}


internal class ApproximateTake : TakeHandler
{
    private ILookup<string, Hold>? _approxLookup;

    public ApproximateTake(ExchangeContext exchangeContext) : base(exchangeContext)
    { }


    protected override IEnumerable<StorageAssignment<Card>> GetAssignments()
    {
        var wants = ExchangeContext.Deck.Wants;

        if (wants.All(w => w.Copies == 0))
        {
            yield break;
        }

        foreach (var want in wants)
        {
            if (want.Copies == 0)
            {
                continue;
            }

            foreach (var assignment in TakeFromStorage(want))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<StorageAssignment<Card>> TakeFromStorage(Want want)
    {
        _approxLookup ??= TakeLookup();

        var namePositions = _approxLookup[want.Card.Name];

        return TakeFromStorage(want.Card, want.Copies, namePositions);
    }

    // take assignments should take from smaller dup stacks first
    // in boxes with less available space

    private ILookup<string, Hold> TakeLookup()
    {
        var targets = TreasuryContext.Holds;

        var cardNames = ExchangeContext.Deck.Wants
            .Select(w => w.Card.Name)
            .Distinct();

        var storageSpace = TreasuryContext.StorageSpace;

        // TODO: account for changing NumCopies while iter
        return targets
            .Join(cardNames,
                h => h.Card.Name, cn => cn,
                (target, _) => target)

            // lookup group orders should preserve NumCopies order
            .OrderBy(h => h.Copies)
                .ThenBy(h => h.Location switch
                {
                    Box box => box.Capacity - storageSpace.GetValueOrDefault(box),
                    Excess excess => -storageSpace.GetValueOrDefault(excess),
                    _ => throw new ArgumentException(nameof(targets))
                })

            .ToLookup(h => h.Card.Name);
    }
}