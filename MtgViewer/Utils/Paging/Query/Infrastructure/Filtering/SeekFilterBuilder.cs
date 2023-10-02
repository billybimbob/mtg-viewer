using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class SeekFilterBuilder
{
    private readonly SeekOrderCollection _orderCollection;
    private readonly SeekDirection _direction;

    public SeekFilterBuilder(SeekOrderCollection orderCollection, SeekDirection direction)
    {
        _orderCollection = orderCollection;
        _direction = direction;
    }

    public LambdaExpression? Build()
    {
        var filterProperties = _orderCollection.BuildFilterProperties();

        if (filterProperties.Count is 0)
        {
            return null;
        }

        var comparisons = filterProperties
            .Select(CompareTo)
            .OfType<BinaryExpression>()
            .ToList();

        if (comparisons.Count is 0)
        {
            return null;
        }

        var filter = comparisons
            .Aggregate(Expression.OrElse);

        return Expression.Lambda(filter, _orderCollection.Parameter);
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

        var previousParameters = orderProperty
            .Skip(1)
            .Select(k => k.Member)
            .OfType<MemberExpression>()
            .Reverse();

        if (EqualTo(previousParameters) is Expression equalTo)
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
        var origin = _orderCollection.Translate(parameter);
        return (origin, nullOrder) switch
        {
            (MemberExpression o and { Type.IsEnum: true }, _) =>
                Expression.GreaterThan(
                    parameter, o, false, TypeHelpers.EnumGreaterThan(o.Type)),

            (MemberExpression o, _) when TypeHelpers.IsValueComparable(o.Type) =>
                Expression.GreaterThan(parameter, o),

            (MemberExpression o, _) when o.Type == typeof(string) =>
                Expression.GreaterThan(
                    Expression.Call(parameter, TypeHelpers.StringCompareTo, o),
                    Expression.Constant(0)),

            (null, NullOrder.Before) =>
                Expression.NotEqual(parameter, Expression.Constant(null)),

            (not null, NullOrder.After) =>
                Expression.Equal(parameter, Expression.Constant(null)),

            (null, NullOrder.None) or _ => null
        };
    }

    private BinaryExpression? LessThan(MemberExpression parameter, NullOrder nullOrder)
    {
        var origin = _orderCollection.Translate(parameter);
        return (origin, nullOrder) switch
        {
            (MemberExpression o and { Type.IsEnum: true }, _) =>
                Expression.LessThan(
                    parameter, o, false, TypeHelpers.EnumLessThan(o.Type)),

            (MemberExpression o, _) when TypeHelpers.IsValueComparable(o.Type) =>
                Expression.LessThan(parameter, o),

            (MemberExpression o, _) when o.Type == typeof(string) =>
                Expression.LessThan(
                    Expression.Call(parameter, TypeHelpers.StringCompareTo, o),
                    Expression.Constant(0)),

            (null, NullOrder.After) =>
                Expression.NotEqual(parameter, Expression.Constant(null)),

            (not null, NullOrder.Before) =>
                Expression.Equal(parameter, Expression.Constant(null)),

            (null, NullOrder.None) or _ => null
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
        var origin = _orderCollection.Translate(parameter);
        return origin switch
        {
            MemberExpression o when TypeHelpers.IsScalarType(o.Type) =>
                Expression.Equal(parameter, o),

            MemberExpression o when o.Type != parameter.Type =>
                Expression.NotEqual(parameter, Expression.Constant(null)),

            null when _orderCollection.IsMemberNull(parameter) =>
                Expression.Equal(parameter, Expression.Constant(null)),

            _ => null
        };
    }
}
