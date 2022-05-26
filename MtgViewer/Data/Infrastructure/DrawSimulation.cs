using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.ObjectPool;

using MtgViewer.Data.Projections;

namespace MtgViewer.Data.Infrastructure;

public sealed class DrawSimulation : IDisposable
{
    // could use dependency injection instead of static ref
    private static readonly ObjectPool<CardCopy> _cardPool
        = new DefaultObjectPool<CardCopy>(
            new DefaultPooledObjectPolicy<CardCopy>());

    private readonly ICollection<CardCopy> _cardOptions;
    private readonly List<CardPreview> _hand;

    private int _cardsInDeck;
    private CardCopy? _nextDraw;

    public DrawSimulation(IReadOnlyList<DeckCopy> deck, DeckMulligan mulligan)
    {
        _cardOptions = deck
            .Select(d =>
            {
                var copy = _cardPool.Get();
                copy.Card = d;
                copy.Copies = GetCopies(d, mulligan);

                return copy;
            })
            .ToHashSet(); // want hash set for undefined (random) iter order

        _hand = new List<CardPreview>();

        _cardsInDeck = deck
            .Sum(d => GetCopies(d, mulligan));

        _nextDraw = PickRandomCard();
    }

    public IReadOnlyList<CardPreview> Hand => _hand;

    public bool CanDraw => _nextDraw is not null;

    public void Dispose()
    {
        foreach (var option in _cardOptions)
        {
            _cardPool.Return(option);
        }

        _cardOptions.Clear();
        _hand.Clear();

        _nextDraw = null;
    }

    public void DrawCard()
    {
        if (_nextDraw is not { Card: CardPreview card })
        {
            return;
        }

        _nextDraw.Copies -= 1;
        _cardsInDeck -= 1;

        _nextDraw = PickRandomCard(); // keep eye on, O(N) could be bottleneck

        _hand.Add(card);
    }

    private CardCopy? PickRandomCard()
    {
        if (_cardsInDeck <= 0)
        {
            return null;
        }

        int picked = Random.Shared.Next(1, _cardsInDeck + 1);

        using var e = _cardOptions.GetEnumerator();

        while (e.MoveNext())
        {
            picked -= e.Current.Copies;

            if (picked <= 0)
            {
                return e.Current;
            }
        }

        return null;
    }

    private static int GetCopies(DeckCopy source, DeckMulligan mulligan)
    {
        return mulligan switch
        {
            DeckMulligan.Built => source.Held,
            DeckMulligan.Theorycraft => source.Held - source.Returning + source.Want,
            DeckMulligan.None or _ => 0
        };
    }
}
