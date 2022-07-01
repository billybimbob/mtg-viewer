using MtgApiManager.Lib.Service;

namespace MtgViewer.Services.Search.Parameters;

internal record PageSize : IMtgParameter
{
    public PageSize() : this(0)
    { }

    private PageSize(int value)
    {
        _value = value;
    }

    private readonly int _value;
    public bool IsEmpty => _value <= 0;

    public IMtgParameter Accept(object? value)
    {
        if (value is int newValue and > 0 and <= MtgApiQuery.Limit)
        {
            return new PageSize(newValue);
        }

        return this;
    }

    public ICardService Apply(ICardService cards)
    {
        if (_value > 0)
        {
            return cards.Where(q => q.PageSize, _value);
        }

        return cards;
    }

    public bool Equals(IMtgParameter? other)
        => Equals(other as PageSize);
}
