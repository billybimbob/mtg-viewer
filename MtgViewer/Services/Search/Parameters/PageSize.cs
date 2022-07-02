using MtgApiManager.Lib.Service;

namespace MtgViewer.Services.Search.Parameters;

internal record PageSize : IMtgParameter
{
    private readonly int _value;

    public PageSize() : this(0)
    { }

    private PageSize(int value)
    {
        _value = value;
    }

    public bool IsEmpty => _value <= 0;

    public IMtgParameter From(object? value)
    {
        if (value is int newValue and > 0 and <= MtgApiQuery.Limit)
        {
            return new PageSize(newValue);
        }

        return this;
    }

    public ICardService ApplyTo(ICardService cards)
    {
        if (IsEmpty)
        {
            return cards;
        }

        return cards.Where(q => q.PageSize, _value);
    }

    public bool Equals(IMtgParameter? other)
        => Equals(other as PageSize);
}
