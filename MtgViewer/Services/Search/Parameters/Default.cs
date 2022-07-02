using System;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

using MtgApiManager.Lib.Service;

namespace MtgViewer.Services.Search.Parameters;

internal class Default : IMtgParameter
{
    public Default(Expression<Func<CardQueryParameter, string>> property)
        : this(property, string.Empty)
    {
    }

    private Default(Expression<Func<CardQueryParameter, string>> property, string value)
    {
        _property = property;
        _value = value;
    }

    private readonly Expression<Func<CardQueryParameter, string>> _property;
    private readonly string _value;

    public bool IsEmpty => string.IsNullOrWhiteSpace(_value);

    public IMtgParameter From(object? value)
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

        if (toString is null)
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

    public ICardService ApplyTo(ICardService cards)
    {
        if (IsEmpty)
        {
            return cards;
        }

        return cards.Where(_property, _value);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(_value);
        hash.Add(_property, ExpressionEqualityComparer.Instance);

        return hash.ToHashCode();
    }

    public bool Equals(IMtgParameter? other)
        => other is Default otherDefault
            && string.Equals(_value, otherDefault._value, StringComparison.Ordinal)
            && ExpressionEqualityComparer.Instance.Equals(_property, otherDefault._property);

    public override bool Equals(object? obj)
        => Equals(obj as IMtgParameter);
}
