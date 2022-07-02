using System.Linq;

using MtgApiManager.Lib.Service;

using MtgViewer.Data;

namespace MtgViewer.Services.Search.Parameters;

internal record Color : IMtgParameter
{
    private readonly Data.Color _value;

    public Color() : this(Data.Color.None)
    { }

    private Color(Data.Color value)
    {
        _value = value;
    }

    public bool IsEmpty => _value is Data.Color.None;

    public IMtgParameter From(object? value)
    {
        if (value is Data.Color newValue and not Data.Color.None)
        {
            return new Color(newValue);
        }

        return this;
    }

    public ICardService ApplyTo(ICardService cards)
    {
        if (IsEmpty)
        {
            return cards;
        }

        var names = Symbol.Colors
            .Where(kv => _value.HasFlag(kv.Key))
            .Select(kv => kv.Value);

        string value = string.Join(MtgApiQuery.And, names);

        return cards.Where(q => q.ColorIdentity, value);
    }

    public bool Equals(IMtgParameter? other)
        => Equals(other as Color);
}
