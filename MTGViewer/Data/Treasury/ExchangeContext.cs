using System;
using System.Collections.Generic;
using System.Linq;

using MTGViewer.Data.Infrastructure;

namespace MTGViewer.Data.Treasury;

internal sealed partial class ExchangeContext
{
    private readonly CardDbContext _dbContext;
    private readonly Dictionary<string, QuantityGroup> _deckCards;

    public ExchangeContext(CardDbContext dbContext, TreasuryContext treasuryContext)
    {
        Deck = dbContext.Decks.Local.First();

        TreasuryContext = treasuryContext;

        _dbContext = dbContext;

        _deckCards = QuantityGroup
            .FromDeck(Deck)
            .ToDictionary(q => q.CardId);
    }

    public TreasuryContext TreasuryContext { get; }

    public Deck Deck { get; }

    public void TakeCopies(Card card, int copies, Storage storage)
    {
        var wants = GetPossibleWants(card).ToList();

        if (wants.Sum(w => w.Copies) < copies)
        {
            throw new ArgumentException("Amount of taken copies is too high", nameof(copies));
        }

        using var e = wants.GetEnumerator();
        int wantCopies = copies;

        while (wantCopies > 0 && e.MoveNext())
        {
            var want = e.Current;
            int minTransfer = Math.Min(wantCopies, want.Copies);

            want.Copies -= minTransfer;
            wantCopies -= minTransfer;
        }

        var hold = GetOrAddHold(card);

        hold.Copies += copies;

        TreasuryContext.TransferCopies(card, copies, Deck, storage);
    }

    private IEnumerable<Want> GetPossibleWants(Card card)
    {
        var approxWants = Deck.Wants
            .Where(w => w.CardId != card.Id
                && w.Card.Name == card.Name
                && w.Copies > 0);

        if (_deckCards.GetValueOrDefault(card.Id)
            is { Want: Want exactWant }
            && exactWant.Copies > 0)
        {
            return approxWants.Prepend(exactWant);
        }

        return approxWants;
    }

    private Hold GetOrAddHold(Card card)
    {
        if (_deckCards.TryGetValue(card.Id, out var group)
            && group is { Hold: Hold hold })
        {
            return hold;
        }

        hold = new Hold
        {
            Card = card,
            Location = Deck
        };

        _dbContext.Holds.Attach(hold);

        if (group is not null)
        {
            group.Hold = hold;
        }
        else
        {
            group = new QuantityGroup(hold);

            _deckCards.Add(card.Id, group);
        }

        return hold;
    }

    public void ReturnCopies(Card card, int copies, Storage storage)
    {
        if (_deckCards.GetValueOrDefault(card.Id)
            is not { Giveback: Giveback give, Hold: Hold hold })
        {
            throw new ArgumentException("Card cannot be found", nameof(card));
        }

        if (give.Copies < copies || hold.Copies < copies)
        {
            throw new ArgumentException("Return amount is higher than giveback or hold", nameof(copies));
        }

        give.Copies -= copies;
        hold.Copies -= copies;

        TreasuryContext.TransferCopies(card, copies, storage, Deck);
    }
}
