using System;
using System.Collections.Generic;
using System.Linq;

using MtgApiManager.Lib.Service;

namespace MtgViewer.Services.Search.Parameters;

internal class Types : IMtgParameter
{
    public Types() : this(Array.Empty<string>())
    { }

    private Types(string[] types)
    {
        _value = types;
    }

    private readonly string[] _value;
    public bool IsEmpty => _value.Length is 0;

    public IMtgParameter Accept(object? value)
    {
        if (value is IEnumerable<string> values)
        {
            string[] types = values
                .Select(Validate)
                .OfType<string>()
                .Concat(_value)
                .ToArray();

            return new Types(types);
        }
        else if (Validate(value?.ToString()) is string valid)
        {
            return new Types(
                valid.Split().Concat(_value).ToArray());
        }

        return this;
    }

    private string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    public ICardService Apply(ICardService cards)
    {
        if (!_value.Any())
        {
            return cards;
        }

        string types = string.Join(MtgApiQuery.And, _value);

        return cards.Where(q => q.Type, types);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (string type in _value)
        {
            hash.Add(type);
        }

        return hash.ToHashCode();
    }

    public bool Equals(IMtgParameter? other)
        => other is Types { _value: string[] otherValue } && _value.SequenceEqual(otherValue);

    public override bool Equals(object? obj)
        => Equals(obj as IMtgParameter);
}
