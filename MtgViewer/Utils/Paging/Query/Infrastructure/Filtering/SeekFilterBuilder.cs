using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class SeekFilterBuilder
{
    private readonly SeekOrderCollection _orderCollection;
    private readonly OriginTranslator _originTranslator;
    private readonly SeekDirection _direction;

    public SeekFilterBuilder(SeekOrderCollection orderCollection, OriginTranslator originTranslator, SeekDirection direction)
    {
        _orderCollection = orderCollection;
        _originTranslator = originTranslator;
        _direction = direction;
    }

    public LambdaExpression? Build()
    {
        if (_orderCollection.OrderProperties.Count is 0)
        {
            return null;
        }

        var comparisons = _orderCollection.OrderProperties
            .Select(CompareTo)
            .OfType<BinaryExpression>()
            .ToList();

        if (comparisons.Count is 0)
        {
            return null;
        }

        var filter = comparisons
            .Aggregate(Expression.OrElse);

        return Expression
            .Lambda(filter, _orderCollection.Parameter);
    }

    private BinaryExpression? CompareTo(LinkedOrderProperty orderProperty)
    {
        var (member, ordering, nullOrder) = orderProperty;

        if (member is null)
        {
            return null;
        }

        var comparison = IsGreaterThan(ordering)
            ? GreaterThan(member, nullOrder)
            : LessThan(member, nullOrder);

        if (comparison is null)
        {
            return null;
        }

        var previousProperties = orderProperty
            .Skip(1)
            .Select(k => k.Member)
            .OfType<MemberExpression>()
            .Reverse();

        if (EqualTo(previousProperties) is Expression equalTo)
        {
            return Expression.AndAlso(equalTo, comparison);
        }

        return comparison;
    }

    private bool IsGreaterThan(Ordering ordering)
        => (_direction, ordering)
            is (SeekDirection.Forward, Ordering.Ascending)
            or (SeekDirection.Backwards, Ordering.Descending);

    private BinaryExpression? GreaterThan(MemberExpression parameter, NullOrder nullOrder)
    {
        return _originTranslator.Translate(parameter) switch
        {
            MemberExpression { Type: var t } origin when TypeHelpers.IsValueComparable(t) =>
                Expression.GreaterThan(parameter, origin),

            MemberExpression { Type: var t } origin when t.IsEnum =>
                Expression.GreaterThan(parameter, origin, false, TypeHelpers.EnumGreaterThan.MakeGenericMethod(t)),

            MemberExpression { Type: var t } origin when t == typeof(string) =>
                Expression.GreaterThan(
                    Expression.Call(parameter, TypeHelpers.StringCompareTo, origin),
                    Expression.Constant(0)),

            null when nullOrder is NullOrder.Before =>
                Expression.NotEqual(parameter, Expression.Constant(null)),

            not null when nullOrder is NullOrder.After =>
                Expression.Equal(parameter, Expression.Constant(null)),

            _ => null
        };
    }

    private BinaryExpression? LessThan(MemberExpression parameter, NullOrder nullOrder)
    {
        return _originTranslator.Translate(parameter) switch
        {
            MemberExpression { Type: var t } origin when TypeHelpers.IsValueComparable(t) =>
                Expression.LessThan(parameter, origin),

            MemberExpression { Type: var t } origin when t.IsEnum =>
                Expression.LessThan(parameter, origin, false, TypeHelpers.EnumLessThan.MakeGenericMethod(t)),

            MemberExpression { Type: var t } origin when t == typeof(string) =>
                Expression.LessThan(
                    Expression.Call(parameter, TypeHelpers.StringCompareTo, origin),
                    Expression.Constant(0)),

            null when nullOrder is NullOrder.After =>
                Expression.NotEqual(parameter, Expression.Constant(null)),

            not null when nullOrder is NullOrder.Before =>
                Expression.Equal(parameter, Expression.Constant(null)),

            _ => null
        };
    }

    private BinaryExpression? EqualTo(IEnumerable<MemberExpression> parameters)
    {
        var equals = parameters
            .Select(EqualTo)
            .OfType<BinaryExpression>()
            .ToList();

        if (equals.Count is 0)
        {
            return null;
        }

        return equals.Aggregate(Expression.AndAlso);
    }

    private BinaryExpression? EqualTo(MemberExpression parameter)
    {
        return _originTranslator.Translate(parameter) switch
        {
            MemberExpression { Type: var t } origin when TypeHelpers.IsScalarType(t) =>
                Expression.Equal(parameter, origin),

            MemberExpression { Type: var t } when t != parameter.Type =>
                Expression.NotEqual(parameter, Expression.Constant(null)),

            null when _originTranslator.IsMemberNull(parameter) =>
                Expression.Equal(parameter, Expression.Constant(null)),

            _ => null
        };
    }
}
