using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.ObjectPool;

using MtgViewer.Data.Projections;

namespace MtgViewer.Data.Infrastructure;

public sealed class DrawSimulation : IDisposable
{
    // could use dependency injection instead of static ref
    private static readonly ObjectPool<MulliganOption> _cardPool
        = new DefaultObjectPool<MulliganOption>(
            new DefaultPooledObjectPolicy<MulliganOption>());

    private readonly ICollection<MulliganOption> _cardOptions;
    private readonly List<CardPreview> _hand;

    private int _cardsInDeck;
    private MulliganOption? _nextDraw;

    public DrawSimulation(IReadOnlyList<DeckCopy> deck, DeckMulligan mulligan)
    {
        _cardOptions = deck
            .Select(d =>
            {
                var option = _cardPool.Get();
                option.Card = d;
                option.Copies = GetCopies(d, mulligan);

                return option;
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

    private MulliganOption? PickRandomCard()
    {
        if (_cardsInDeck <= 0)
        {
            return null;
        }

        int start = Random.Shared.Next(0, _cardOptions.Count);
        int picked = Random.Shared.Next(1, _cardsInDeck + 1);

        using var e = _cardOptions
            .Skip(start)
            .Concat(_cardOptions.Take(start))
            .GetEnumerator();

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
