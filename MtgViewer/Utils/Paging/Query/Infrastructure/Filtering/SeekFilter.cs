using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

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
        if (!_orderKeys.Any())
        {
            return null;
        }

        var firstKey = _orderKeys[0];

        var otherKeys = _orderKeys
            .Select((key, i) => (key, i))
            .Skip(1);

        var filter = CompareTo(firstKey);

        foreach ((var key, int before) in otherKeys)
        {
            var comparison = CompareTo(key, _orderKeys.Take(before));

            if (comparison is null)
            {
                continue;
            }

            if (filter is null)
            {
                filter = comparison;
                continue;
            }

            filter = Expression.OrElse(filter, comparison);
        }

        if (filter is null)
        {
            return null;
        }

        return Expression.Lambda(filter, _parameter);
    }

    private NullOrder GetNullOrder(MemberExpression node)
        => _nullOrders.GetValueOrDefault(node);

    private BinaryExpression? CompareTo(KeyOrder keyOrder, IEnumerable<KeyOrder>? beforeKeys = null)
    {
        var (parameter, ordering) = keyOrder;

        if (parameter is null)
        {
            return null;
        }

        var comparison = IsGreaterThan(ordering)
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

    private bool IsGreaterThan(Ordering ordering)
        => (_direction, ordering)
            is (SeekDirection.Forward, Ordering.Ascending)
            or (SeekDirection.Backwards, Ordering.Descending);

    private BinaryExpression? GreaterThan(MemberExpression parameter)
    {
        return (_origin.Translate(parameter), GetNullOrder(parameter)) switch
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
        return (_origin.Translate(parameter), GetNullOrder(parameter)) switch
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
            .OfType<BinaryExpression>();

        if (!equals.Any())
        {
            return null;
        }

        return equals.Aggregate(Expression.AndAlso);
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

    #region Origin Builder types

    private sealed class FindOrderPropertiesVisitor : ExpressionVisitor
    {
        public static IReadOnlyList<KeyOrder> Scan(
            OrderPropertyVisitor orderByVisitor,
            OriginTranslator origin,
            Expression expression)
        {
            var findOrders = new FindOrderPropertiesVisitor(orderByVisitor, origin);

            _ = findOrders.Visit(expression);

            return findOrders._properties;
        }

        private readonly OrderPropertyVisitor _orderProperty;
        private readonly OriginTranslator _origin;

        private readonly List<KeyOrder> _properties;

        private FindOrderPropertiesVisitor(OrderPropertyVisitor orderProperty, OriginTranslator origin)
        {
            _orderProperty = orderProperty;
            _origin = origin;

            _properties = new List<KeyOrder>();
        }

        [return: NotNullIfNotNull("node")]
        public override Expression? Visit(Expression? node)
        {
            _properties.Clear();

            return base.Visit(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (!ExpressionHelpers.IsOrderBy(node)
                && node.Arguments.ElementAtOrDefault(0) is Expression parent)
            {
                _ = base.Visit(parent);
            }

            if (_orderProperty.Visit(node) is MemberExpression propertyOrder
                && _origin.TryRegister(propertyOrder))
            {
                var ordering = ExpressionHelpers.IsDescending(node)
                    ? Ordering.Descending
                    : Ordering.Ascending;

                _properties.Add(new KeyOrder(propertyOrder, ordering));
            }

            return node;
        }
    }

    private sealed class FindNullPropertiesVisitor : ExpressionVisitor
    {
        public static IReadOnlyDictionary<Expression, NullOrder> Scan(
            OrderPropertyVisitor orderByVisitor,
            OriginTranslator origin,
            Expression expression)
        {
            var findNulls = new FindNullPropertiesVisitor(orderByVisitor, origin);

            _ = findNulls.Visit(expression);

            return findNulls._properties;
        }

        private readonly OrderPropertyVisitor _orderProperty;
        private readonly OriginTranslator _origin;

        private readonly Dictionary<Expression, NullOrder> _properties;

        private MemberExpression? _lastProperty;

        private FindNullPropertiesVisitor(OrderPropertyVisitor orderProperty, OriginTranslator origin)
        {
            _orderProperty = orderProperty;
            _origin = origin;

            _properties = new Dictionary<Expression, NullOrder>(ExpressionEqualityComparer.Instance);
        }

        [return: NotNullIfNotNull("node")]
        public override Expression? Visit(Expression? node)
        {
            _lastProperty = null;
            _properties.Clear();

            return base.Visit(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (!ExpressionHelpers.IsOrderBy(node)
                && node.Arguments.ElementAtOrDefault(0) is Expression parent)
            {
                _ = base.Visit(parent);
            }

            if (_orderProperty.Visit(node) is not MemberExpression nullOrder
                || !_origin.TryRegister(nullOrder))
            {
                return node;
            }

            // a null is only used as a sort property if there are also
            // non null orderings specified

            // null check ordering by itself it not unique enough

            if (!ExpressionHelpers.IsDescendant(_lastProperty, nullOrder))
            {
                _lastProperty = nullOrder;

                return node;
            }

            var ordering = ExpressionHelpers.IsDescending(node)
                ? NullOrder.Before
                : NullOrder.After;

            _properties.Add(nullOrder, ordering);

            return node;
        }
    }

    #endregion
}
