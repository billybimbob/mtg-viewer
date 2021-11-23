using System;
namespace MTGViewer.Data;

public readonly record struct QuantityIndex(string CardId, int LocationId)
{
    public string CardId { get; } = CardId;
    public int LocationId { get; } = LocationId;

    public static explicit operator QuantityIndex(Quantity quantity)
    {
        return new(quantity.CardId, quantity.LocationId);
    }

    public static implicit operator QuantityIndex(ValueTuple<string,int> valueTuple)
    {
        (string cardId, int locationId) = valueTuple;

        return new(cardId, locationId);
    }

    public static implicit operator ValueTuple<string,int>(QuantityIndex quantityIndex)
    {
        (string cardId, int locationId) = quantityIndex;

        return (cardId, locationId);
    }
}