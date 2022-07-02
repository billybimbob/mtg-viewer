using System;

using MtgApiManager.Lib.Service;

namespace MtgViewer.Services.Search.Parameters;

internal record Rarity : IMtgParameter
{
    private readonly Data.Rarity? _value;

    public Rarity() : this(null as Data.Rarity?)
    { }

    private Rarity(Data.Rarity? value)
    {
        _value = value;
    }

    public bool IsEmpty => _value is null;

    public IMtgParameter From(object? value)
    {
        if (value is Data.Rarity newValue && Enum.IsDefined(newValue))
        {
            return new Rarity(newValue);
        }

        return this;
    }

    public ICardService ApplyTo(ICardService cards)
    {
        if (_value is not Data.Rarity value)
        {
            return cards;
        }

        return cards.Where(q => q.Rarity, value.ToString());
    }

    public bool Equals(IMtgParameter? other)
        => Equals(other as Rarity);
}
