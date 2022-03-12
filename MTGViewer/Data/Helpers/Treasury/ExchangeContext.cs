using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Internal;

internal class ExchangeContext
{
    private readonly CardDbContext _dbContext;
    private readonly Dictionary<string, QuantityGroup> _deckCards;

    public ExchangeContext(
        CardDbContext dbContext, TreasuryContext treasuryContext)
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


    public void TakeCopies(Card card, int numCopies, Storage storage)
    {
        var wants = GetPossibleWants(card).ToList();

        if (wants.Sum(w => w.Copies) < numCopies)
        {
            throw new ArgumentException(nameof(numCopies));
        }

        using var e = wants.GetEnumerator();
        int wantCopies = numCopies;

        while (wantCopies > 0 && e.MoveNext())
        {
            var want = e.Current;
            int minTransfer = Math.Min(wantCopies, want.Copies);

            want.Copies -= minTransfer;
            wantCopies -= minTransfer;
        }

        var amount = GetOrAddAmount(card);
        amount.Copies += numCopies;

        TreasuryContext.TransferCopies(card, numCopies, Deck, storage);
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


    private Amount GetOrAddAmount(Card card)
    {
        if (_deckCards.TryGetValue(card.Id, out var group)
            && group is { Amount: Amount amount })
        {
            return amount;
        }

        amount = new Amount
        {
            Card = card,
            Location = Deck
        };

        _dbContext.Amounts.Attach(amount);

        if (group is not null)
        {
            group.Amount = amount;
        }
        else
        {
            group = new(amount);
            _deckCards.Add(card.Id, group);
        }

        return amount;
    }


    public void ReturnCopies(Card card, int numCopies, Storage storage)
    {
        if (_deckCards.GetValueOrDefault(card.Id)
            is not { GiveBack: GiveBack give, Amount: Amount amount }
            || give.Copies < numCopies
            || amount.Copies < numCopies)
        {
            throw new ArgumentException($"{nameof(card)} and {nameof(numCopies)}");
        }

        give.Copies -= numCopies;
        amount.Copies -= numCopies;

        TreasuryContext.TransferCopies(card, numCopies, storage, Deck);
    }
}
