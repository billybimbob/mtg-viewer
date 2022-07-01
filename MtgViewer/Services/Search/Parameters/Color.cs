using System.Linq;

using MtgApiManager.Lib.Service;

using MtgViewer.Data;

namespace MtgViewer.Services.Search.Parameters;

internal record Color : IMtgParameter
{
    public Color() : this(Data.Color.None)
    { }

    private Color(Data.Color value)
    {
        _value = value;
    }

    private readonly Data.Color _value;
    public bool IsEmpty => _value is Data.Color.None;

    public IMtgParameter Accept(object? value)
    {
        if (value is Data.Color newValue and not Data.Color.None)
        {
            return new Color(newValue);
        }

        return this;
    }

    public ICardService Apply(ICardService cards)
    {
        if (_value is Data.Color.None)
        {
            return cards;
        }

        var colorNames = Symbol.Colors
            .Where(kv => _value.HasFlag(kv.Key))
            .Select(kv => kv.Value);

        string colors = string.Join(MtgApiQuery.And, colorNames);

        return cards.Where(q => q.ColorIdentity, colors);
    }

    public bool Equals(IMtgParameter? other)
        => Equals(other as Color);
}
