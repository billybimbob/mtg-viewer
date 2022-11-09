using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filter;

internal sealed class SeekFilter
{
    public static LambdaExpression? Build(Expression query, ConstantExpression? origin, SeekDirection? direction)
    {
        if (origin is null)
        {
            return null;
        }

        if (direction is not SeekDirection dir)
        {
            return null;
        }

        var builder = new SeekFilter(query, origin, dir);

        return builder.Build();
    }

    private readonly ParameterExpression _parameter;

    private readonly OriginTranslator _origin;
    private readonly SeekDirection _direction;

    private readonly IReadOnlyList<KeyOrder> _orderKeys;
    private readonly IReadOnlyDictionary<Expression, NullOrder> _nullOrders;

    private SeekFilter(Expression query, OriginTranslator origin, SeekDirection direction)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(origin);

        _parameter = Expression
            .Parameter(
                origin.Type,
                origin.Type.Name[0].ToString().ToLowerInvariant());

        _origin = origin;
        _direction = direction;

        var orderProperty = new OrderPropertyVisitor(_parameter);

        _orderKeys = FindOrderPropertiesVisitor.Scan(orderProperty, origin, query);
        _nullOrders = FindNullPropertiesVisitor.Scan(orderProperty, origin, query);
    }

    private SeekFilter(Expression query, ConstantExpression origin, SeekDirection direction)
        : this(query, new OriginTranslator(origin), direction)
    {
    }

    private LambdaExpression? Build()
    {
        if (_orderKeys is not [KeyOrder first, ..])
        {
            return null;
        }

        var otherKeys = _orderKeys
            .Select((key, i) => (key, i))
            .Skip(1);

        var filter = CompareTo(first);

        foreach ((var key, int before) in otherKeys)
        {
            var comparison = CompareTo(key, _orderKeys.Take(before));

            if (comparison is null)
            {
                continue;
            }

            filter = filter is null
                ? comparison
                : Expression.OrElse(filter, comparison);

        }

        if (filter is null)
        {
            return null;
        }

        return Expression.Lambda(filter, _parameter);
    }

    private BinaryExpression? CompareTo(KeyOrder keyOrder, IEnumerable<KeyOrder>? beforeKeys = null)
    {
        var (parameter, ordering) = keyOrder;

        if (parameter is null)
        {
            return null;
        }

        bool isGreaterThan = (_direction, ordering)
            is (SeekDirection.Forward, Ordering.Ascending)
            or (SeekDirection.Backwards, Ordering.Descending);

        var comparison = isGreaterThan
            ? GreaterThan(parameter)
            : LessThan(parameter);

        if (comparison is null)
        {
            return null;
        }

        var equalKeys = beforeKeys
            ?.Select(k => k.Key)
            .OfType<MemberExpression>()
            ?? Enumerable.Empty<MemberExpression>();

        if (EqualTo(equalKeys) is Expression equalTo)
        {
            return Expression.AndAlso(equalTo, comparison);
        }

        return comparison;
    }

    private BinaryExpression? GreaterThan(MemberExpression parameter)
    {
        return (_origin.Translate(parameter), _nullOrders.GetValueOrDefault(parameter)) switch
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

    private BinaryExpression? LessThan(MemberExpression parameter)
    {
        return (_origin.Translate(parameter), _nullOrders.GetValueOrDefault(parameter)) switch
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
            .ToArray();

        if (equals is not [var first, .. var rest])
        {
            return null;
        }

        return rest.Aggregate(first, Expression.AndAlso);
    }

    private BinaryExpression? EqualTo(MemberExpression parameter)
    {
        return _origin.Translate(parameter) switch
        {
            MemberExpression o when TypeHelpers.IsScalarType(o.Type) =>
                Expression.Equal(parameter, o),

            MemberExpression o when o.Type != parameter.Type =>
                Expression.NotEqual(parameter, Expression.Constant(null)),

            null when !_origin.IsParentNull(parameter) =>
                Expression.Equal(parameter, Expression.Constant(null)),

            _ => null
        };
    }
}
