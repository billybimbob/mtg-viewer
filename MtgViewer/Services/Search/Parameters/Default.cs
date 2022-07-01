using System;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

using MtgApiManager.Lib.Service;

namespace MtgViewer.Services.Search.Parameters;

internal class Default : IMtgParameter
{
    public Default(Expression<Func<CardQueryParameter, string>> property)
    {
        _property = property;
        _value = null;
    }

    private Default(Expression<Func<CardQueryParameter, string>> property, string value)
    {
        _property = property;
        _value = value;
    }

    private readonly Expression<Func<CardQueryParameter, string>> _property;
    private readonly string? _value;

    public bool IsEmpty => _value is null;

    public IMtgParameter Accept(object? value)
    {
        if (TryToString(value, out string parameter))
        {
            return new Default(_property, parameter);
        }

        return this;
    }

    private static bool TryToString(object? paramValue, out string stringValue)
    {
        string? toString = paramValue?.ToString();

        if (toString == null)
        {
            stringValue = null!;
            return false;
        }

        toString = toString.Split(MtgApiQuery.Or).FirstOrDefault();

        // make sure that only one specific card is searched for

        if (string.IsNullOrWhiteSpace(toString))
        {
            stringValue = null!;
            return false;
        }

        stringValue = toString.Trim();
        return true;
    }

    public ICardService Apply(ICardService cards)
    {
        if (_value is not null)
        {
            return cards.Where(_property, _value);
        }

        return cards;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(_property, ExpressionEqualityComparer.Instance);
        hash.Add(_value);

        return hash.ToHashCode();
    }

    public bool Equals(IMtgParameter? other)
        => other is Default otherDefault
            && string.Equals(_value, otherDefault._value, StringComparison.Ordinal)
            && ExpressionEqualityComparer.Instance.Equals(_property, otherDefault._property);

    public override bool Equals(object? obj)
        => Equals(obj as IMtgParameter);
}
