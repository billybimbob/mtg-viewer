using System;
using System.Collections.Generic;
using System.Linq;
using MTGViewer.Data;

namespace MTGViewer.Services.Internal;

internal class ExchangeContext
{
    private readonly CardDbContext _dbContext;
    private readonly TreasuryContext _treasuryContext;

    private readonly Dictionary<string, QuantityGroup> _deckCards;
    private readonly ILookup<string, string> _cardNames;

    public ExchangeContext(
        CardDbContext dbContext, TreasuryContext treasuryContext)
    {
        Deck = dbContext.Decks.Local.First();

        _dbContext = dbContext;

        _treasuryContext = treasuryContext;

        _deckCards = QuantityGroup
            .FromDeck(Deck)
            .ToDictionary(q => q.CardId);

        _cardNames = _deckCards.Values
            .ToLookup(q => q.Card.Name, q => q.CardId);
    }

    public Deck Deck { get; }


    public void TakeCopies(Card card, int numCopies, Box box)
    {
        var wants = GetPossibleWants(card).ToList();

        if (wants.Sum(w => w.NumCopies) < numCopies)
        {
            throw new ArgumentException(nameof(numCopies));
        }

        using var e = wants.GetEnumerator();
        int wantCopies = numCopies;

        while (wantCopies > 0 && e.MoveNext())
        {
            var want = e.Current;
            int minTransfer = Math.Min(wantCopies, want.NumCopies);

            want.NumCopies -= minTransfer;
            wantCopies -= minTransfer;
        }

        var amount = GetOrAddAmount(card);
        amount.NumCopies += numCopies;

        _treasuryContext.TransferCopies(card, numCopies, Deck, box);
    }


    private IEnumerable<Want> GetPossibleWants(Card card)
    {
        var approxWants = Deck.Wants
            .Where(w => w.CardId != card.Id 
                && w.Card.Name == card.Name
                && w.NumCopies > 0);

        if (_deckCards.TryGetValue(card.Id, out var group)
            && group.Want is Want exactWant
            && exactWant.NumCopies > 0)
        {
            return approxWants.Prepend(exactWant);
        }

        return approxWants;
    }


    private Amount GetOrAddAmount(Card card)
    {
        if (_deckCards.TryGetValue(card.Id, out var group)
            && group.Amount is Amount amount)
        {
            return amount;
        }

        amount = new Amount
        {
            Card = card,
            Location = Deck
        };

        _dbContext.Amounts.Add(amount);

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


    public void ReturnCopies(Card card, int numCopies, Box box)
    {
        if (!_deckCards.TryGetValue(card.Id, out var group)
            || group.GiveBack is not GiveBack give
            || give.NumCopies < numCopies
            || group.Amount is not Amount amount
            || amount.NumCopies < numCopies)
        {
            throw new ArgumentException($"{nameof(card)} and {nameof(numCopies)}");
        }

        give.NumCopies -= numCopies;
        amount.NumCopies -= numCopies;

        _treasuryContext.TransferCopies(card, numCopies, box, Deck);
    }
}