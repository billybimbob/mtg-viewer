using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using MtgApiManager.Lib.Service;

namespace MtgViewer.Services.Search.Parameters;

internal class Types : IMtgParameter
{
    private readonly string[] _value;

    public Types() : this(Array.Empty<string>())
    { }

    private Types(string[] value)
    {
        _value = value;
    }

    public bool IsEmpty => _value.Length is 0;

    public IMtgParameter From(object? value)
    {
        string[] newValue = Validate(value)
            .Concat(_value)
            .ToArray();

        if (newValue.SequenceEqual(_value))
        {
            return this;
        }

        return new Types(newValue);
    }

    private static IEnumerable<string> Validate(object? value)
    {
        return value switch
        {
            IEnumerable<string> items => items
                .SelectMany(Validate),

            IEnumerable items => items
                .Cast<object?>()
                .Select(i => i?.ToString())
                .SelectMany(Validate),

            _ => Validate(value?.ToString())
        };

        static IEnumerable<string> Validate(string? value)
            => value?.Trim().Split() ?? Enumerable.Empty<string>();
    }

    public ICardService ApplyTo(ICardService cards)
    {
        if (IsEmpty)
        {
            return cards;
        }

        string value = string.Join(MtgApiQuery.And, _value);

        return cards.Where(q => q.Type, value);
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
