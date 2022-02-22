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
    protected readonly ExchangeContext _exchangeContext;
    protected readonly TreasuryContext _treasuryContext;

    public TakeHandler(ExchangeContext exchangeContext)
    {
        _exchangeContext = exchangeContext;
        _treasuryContext = exchangeContext.TreasuryContext;
    }

    protected abstract IEnumerable<BoxAssignment<Card>> GetAssignments();

    public void ApplyTakes()
    {
        foreach ((Card card, int numCopies, Box box) in GetAssignments())
        {
            _exchangeContext.TakeCopies(card, numCopies, box);
        }
    }

    protected static IEnumerable<BoxAssignment<TSource>> TakeFromBoxes<TSource>(
        TSource source,
        int cardsToTake,
        IEnumerable<Amount> boxAmounts)
    {
        foreach (var amount in boxAmounts)
        {
            if (amount.Location is not Box box)
            {
                continue;
            }

            int takeCopies = Math.Min(cardsToTake, amount.NumCopies);
            if (takeCopies == 0)
            {
                continue;
            }

            yield return new BoxAssignment<TSource>(source, takeCopies, box);

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
    private ILookup<string, Amount>? _exactTake;

    public ExactTake(ExchangeContext exchangeContext) : base(exchangeContext)
    { }


    protected override IEnumerable<BoxAssignment<Card>> GetAssignments()
    {
        var wants = _exchangeContext.Deck.Wants;

        if (wants.All(w => w.NumCopies == 0))
        {
            yield break;
        }


        foreach (var want in wants)
        {
            foreach (var assignment in TakeFromBoxes(want))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<BoxAssignment<Card>> TakeFromBoxes(Want want)
    {
        _exactTake ??= TakeLookup();

        var idPositions = _exactTake[want.CardId];

        return TakeFromBoxes(want.Card, want.NumCopies, idPositions);
    }

    // take assignments should take from smaller dup stacks first
    // in boxes with less available space

    private ILookup<string, Amount> TakeLookup()
    {
        var targets = _treasuryContext.Amounts;

        var cardIds = _exchangeContext.Deck.Wants
            .Select(w => w.CardId)
            .Distinct();

        var boxSpace = _treasuryContext.BoxSpace;

        // TODO: account for changing NumCopies while iter
        return targets
            .Join( cardIds,
                a => a.CardId, cid => cid,
                (target, _) => target)

            .OrderBy(a => a.NumCopies)
                .ThenBy(a => a.Location switch
                {
                    Box box => box.Capacity - boxSpace.GetValueOrDefault(box),
                    _ => throw new ArgumentException(nameof(targets))
                })
            
            // lookup group orders should preserve NumCopies order
            .ToLookup(a => a.CardId);
    }
}


internal class ApproximateTake : TakeHandler
{
    private ILookup<string, Amount>? _approxLookup;

    public ApproximateTake(ExchangeContext exchangeContext) : base(exchangeContext)
    { }


    protected override IEnumerable<BoxAssignment<Card>> GetAssignments()
    {
        var wants = _exchangeContext.Deck.Wants;

        if (wants.All(w => w.NumCopies == 0))
        {
            yield break;
        }

        foreach (var want in wants)
        {
            if (want.NumCopies == 0)
            {
                continue;
            }

            foreach (var assignment in TakeFromBoxes(want))
            {
                yield return assignment;
            }
        }
    }


    private IEnumerable<BoxAssignment<Card>> TakeFromBoxes(Want want)
    {
        _approxLookup ??= TakeLookup();

        var namePositions = _approxLookup[want.Card.Name];

        return TakeFromBoxes(want.Card, want.NumCopies, namePositions);
    }

    // take assignments should take from smaller dup stacks first
    // in boxes with less available space

    private ILookup<string, Amount> TakeLookup()
    {
        var targets = _treasuryContext.Amounts;

        var cardNames = _exchangeContext.Deck.Wants
            .Select(w => w.Card.Name)
            .Distinct();

        var boxSpace = _treasuryContext.BoxSpace;

        // TODO: account for changing NumCopies while iter
        return targets
            .Join(cardNames,
                a => a.Card.Name, cn => cn,
                (target, _) => target)

            // lookup group orders should preserve NumCopies order
            .OrderBy(a => a.NumCopies)
                .ThenBy(a => a.Location switch
                {
                    Box box => box.Capacity - boxSpace.GetValueOrDefault(box),
                    _ => throw new ArgumentException(nameof(targets))
                })

            .ToLookup(a => a.Card.Name);
    }
}