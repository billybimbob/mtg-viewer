using System;
using MTGViewer.Data;

namespace MTGViewer.Data;

/// <summary>
/// Requests to either take or return  a <see cref="Data.Card"/> to the Treasury
/// </summary>
public record CardRequest(Card Card, int NumCopies)
{
    private Card _card = CardOrThrow(Card);
    private int _numCopies = NotNegativeOrThrow(NumCopies);

    public Card Card
    {
        get => _card;
        init => _card = CardOrThrow(value);
    }

    public int NumCopies
    {
        get => _numCopies;
        set => _numCopies = NotNegativeOrThrow(value);
    }

    private static Card CardOrThrow(Card card) =>
        card ?? throw new ArgumentNullException(nameof(Card));

    private static int NotNegativeOrThrow(int copies) =>
        copies >= 0 ? copies : throw new ArgumentException(nameof(NumCopies));
}
