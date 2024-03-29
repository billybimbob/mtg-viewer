using System;
using System.Collections.Generic;
using System.Linq;

namespace MtgViewer.Data.Treasury.Handlers;

internal abstract class TakeHandler
{
    protected ExchangeContext ExchangeContext { get; }
    protected TreasuryContext TreasuryContext => ExchangeContext.TreasuryContext;

    protected TakeHandler(ExchangeContext exchangeContext)
    {
        ExchangeContext = exchangeContext;
    }

    protected abstract IEnumerable<Assignment<Card>> GetAssignments();

    public void ApplyTakes()
    {
        foreach ((var card, int copies, var target) in GetAssignments())
        {
            ExchangeContext.TakeCopies(card, copies, target);
        }
    }

    protected static IEnumerable<Assignment<Card>> TakeFromStorage(
        int cardsToTake,
        IEnumerable<Hold> storageHolds)
    {
        foreach (var hold in storageHolds)
        {
            if (hold.Location is not Storage storage)
            {
                continue;
            }

            int takeCopies = Math.Min(cardsToTake, hold.Copies);
            if (takeCopies == 0)
            {
                continue;
            }

            yield return new Assignment<Card>(hold.Card, takeCopies, storage);

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
    {
    }

    protected override IEnumerable<Assignment<Card>> GetAssignments()
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

    private IEnumerable<Assignment<Card>> TakeFromStorage(Want want)
    {
        _exactTake ??= TakeLookup();

        var matches = _exactTake[want.CardId];

        return TakeFromStorage(want.Copies, matches);
    }

    // take assignments should take from smaller dup stacks first
    // in boxes with less available space

    private ILookup<string, Hold> TakeLookup()
    {
        var targets = TreasuryContext.Holds;

        var cardIds = ExchangeContext.Deck.Wants
            .Select(w => w.CardId)
            .Distinct();

        var storageSpaces = TreasuryContext.StorageSpaces;

        // TODO: account for changing Copies while iter
        // lookup group orders should preserve Copies order
        return targets
            .Join(cardIds,
                h => h.CardId, cid => cid,
                (target, _) => target)

            .OrderByDescending(h => h.Location is Box)
                .ThenBy(h => h.Copies)

            .ToLookup(h => h.CardId);
    }
}

internal class ApproximateTake : TakeHandler
{
    private ILookup<string, Hold>? _approxLookup;

    public ApproximateTake(ExchangeContext exchangeContext) : base(exchangeContext)
    {
    }

    protected override IEnumerable<Assignment<Card>> GetAssignments()
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

    private IEnumerable<Assignment<Card>> TakeFromStorage(Want want)
    {
        _approxLookup ??= TakeLookup();

        var matches = _approxLookup[want.Card.Name];

        return TakeFromStorage(want.Copies, matches);
    }

    // take assignments should take from smaller dup stacks first
    // in boxes with less available space

    private ILookup<string, Hold> TakeLookup()
    {
        var targets = TreasuryContext.Holds;

        var cardNames = ExchangeContext.Deck.Wants
            .Select(w => w.Card.Name)
            .Distinct();

        var storageSpaces = TreasuryContext.StorageSpaces;

        // TODO: account for changing Copies while iter
        // lookup group orders should preserve Copies order
        return targets
            .Join(cardNames,
                h => h.Card.Name, cn => cn,
                (target, _) => target)

            .OrderByDescending(h => h.Location is Box)
                .ThenBy(h => h.Copies)

            .ToLookup(h => h.Card.Name);
    }
}
