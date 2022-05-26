using System;

namespace MtgViewer.Data;

/// <summary>
/// Requests to either take or return  a <see cref="Data.Card"/> to the Treasury
/// </summary>
public record CardRequest(Card Card, int Copies)
{
    private Card _card = CardOrThrow(Card);
    private int _copies = NotNegativeOrThrow(Copies);

    public Card Card
    {
        get => _card;
        init => _card = CardOrThrow(value);
    }

    public int Copies
    {
        get => _copies;
        set => _copies = NotNegativeOrThrow(value);
    }

    private static Card CardOrThrow(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card;
    }

    private static int NotNegativeOrThrow(int copies) =>
        copies >= 0
            ? copies
            : throw new ArgumentException("Copies is negative", nameof(copies));
}
