using MtgApiManager.Lib.Service;

namespace MtgViewer.Services.Search.Parameters;

internal record Page : IMtgParameter
{
    public Page() : this(0)
    { }

    private Page(int value)
    {
        Value = value;
    }

    public int Value { get; }
    public bool IsEmpty => Value is 0;

    public IMtgParameter Accept(object? value)
    {
        if (value is int newValue and > 0)
        {
            return new Page(newValue);
        }

        return this;
    }

    public ICardService Apply(ICardService cards)
    {
        if (Value > 0)
        {
            // query starts at index 1 instead of 0
            return cards.Where(q => q.Page, Value + 1);
        }

        return cards;
    }

    public bool Equals(IMtgParameter? other)
        => Equals(other as Page);
}
