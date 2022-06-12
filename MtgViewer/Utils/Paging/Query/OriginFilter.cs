using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal static class OriginFilter
{
    public static Expression<Func<TEntity, bool>>? Build<TEntity>(
        IQueryable<TEntity> query,
        TEntity? origin,
        SeekDirection direction)
    {
        var translator = new OriginTranslator<TEntity, TEntity>(origin, null);

        return OriginFilter<TEntity, TEntity>.Build(query, translator, direction);
    }

    // public static Expression<Func<TEntity, bool>>? Build<TEntity, TOrigin>(
    //     IQueryable<TEntity> query,
    //     TOrigin? origin,
    //     SeekDirection direction,
    //     Expression<Func<TEntity, TOrigin>> selector)
    // {
    //     var translator = new OriginTranslator<TOrigin, TEntity>(origin, selector);

    //     return OriginFilter<TOrigin, TEntity>.Build(query, translator, direction);
    // }
}

internal sealed class OriginFilter<TOrigin, TEntity>
{
    internal static Expression<Func<TEntity, bool>>? Build(
        IQueryable<TEntity> query,
        OriginTranslator<TOrigin, TEntity> origin,
        SeekDirection direction)
    {
        var builder = new OriginFilter<TOrigin, TEntity>(query, origin, direction);

        if (!builder.OrderKeys.Any())
        {
            return null;
        }

        var firstKey = builder.OrderKeys[0];

        var otherKeys = builder.OrderKeys
            .Select((key, i) => (key, i))
            .Skip(1);

        var filter = builder.CompareTo(firstKey);

        foreach ((var key, int before) in otherKeys)
        {
            var comparison = builder.CompareTo(key, builder.OrderKeys.Take(before));

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

        return Expression.Lambda<Func<TEntity, bool>>(filter, Parameter);
    }

    private readonly IQueryable<TEntity> _query;
    private readonly OriginTranslator<TOrigin, TEntity> _origin;
    private readonly SeekDirection _direction;

    private OriginFilter(
        IQueryable<TEntity> query,
        OriginTranslator<TOrigin, TEntity> origin,
        SeekDirection direction)
    {
        ArgumentNullException.ThrowIfNull(origin);

        _query = query;
        _origin = origin;
        _direction = direction;
    }

    private static ParameterExpression? _parameter;
    private IReadOnlyList<KeyOrder>? _orderKeys;
    private IReadOnlyDictionary<Expression, NullOrder>? _nullOrders;

    private static ParameterExpression Parameter =>
        _parameter ??=
            Expression.Parameter(
                typeof(TEntity),
                typeof(TEntity).Name[0].ToString().ToLowerInvariant());

    private IReadOnlyList<KeyOrder> OrderKeys
    {
        get
        {
            if (_orderKeys is null)
            {
                var findOrders = new FindOrderPropertiesVisitor(_origin);

                _ = findOrders.Visit(_query.Expression);

                _orderKeys = findOrders.Properties;
            }

            return _orderKeys;
        }
    }

    private NullOrder GetNullOrder(MemberExpression node)
    {
        if (_nullOrders is null)
        {
            var findNulls = new FindNullPropertiesVisitor(_origin);

            _ = findNulls.Visit(_query.Expression);

            _nullOrders = findNulls.Properties;
        }

        return _nullOrders.GetValueOrDefault(node);
    }

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
    {
        return (_direction, ordering)
            is (SeekDirection.Forward, Ordering.Ascending)
            or (SeekDirection.Backwards, Ordering.Descending);
    }

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
                    ExpressionHelpers.Zero),

            (null, NullOrder.Before) =>
                Expression.NotEqual(parameter, ExpressionHelpers.Null),

            (not null, NullOrder.After) =>
                Expression.Equal(parameter, ExpressionHelpers.Null),

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
                    ExpressionHelpers.Zero),

            (null, NullOrder.After) =>
                Expression.NotEqual(parameter, ExpressionHelpers.Null),

            (not null, NullOrder.Before) =>
                Expression.Equal(parameter, ExpressionHelpers.Null),

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
                Expression.NotEqual(parameter, ExpressionHelpers.Null),

            null when !_origin.IsParentNull(parameter) =>
                Expression.Equal(parameter, ExpressionHelpers.Null),

            _ => null
        };
    }

    #region Origin Builder types

    private readonly record struct KeyOrder(MemberExpression? Key, Ordering Ordering);

    private enum Ordering
    {
        Ascending,
        Descending,
    }

    private enum NullOrder
    {
        None,
        Before,
        After
    }

    private sealed class FindOrderPropertiesVisitor : ExpressionVisitor
    {
        private readonly OriginTranslator<TOrigin, TEntity> _origin;
        private readonly List<KeyOrder> _properties;

        public FindOrderPropertiesVisitor(OriginTranslator<TOrigin, TEntity> origin)
        {
            _origin = origin;
            _properties = new List<KeyOrder>();
        }

        public IReadOnlyList<KeyOrder> Properties => _properties;

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

            if (OrderByVisitor.Instance.Visit(node) is MemberExpression propertyOrder
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
        private readonly OriginTranslator<TOrigin, TEntity> _origin;
        private readonly Dictionary<Expression, NullOrder> _properties;
        private MemberExpression? _lastProperty;

        public FindNullPropertiesVisitor(OriginTranslator<TOrigin, TEntity> origin)
        {
            _origin = origin;
            _properties = new Dictionary<Expression, NullOrder>(ExpressionEqualityComparer.Instance);
        }

        public IReadOnlyDictionary<Expression, NullOrder> Properties => _properties;

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

            if (OrderByVisitor.Instance.Visit(node) is not MemberExpression nullOrder
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

    private sealed class OrderByVisitor : ExpressionVisitor
    {
        private static OrderByVisitor? _instance;
        public static OrderByVisitor Instance => _instance ??= new();

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (ExpressionHelpers.IsOrderedMethod(node) && node.Arguments.Count == 2)
            {
                return Visit(node.Arguments[1]);
            }

            return node;
        }

        protected override Expression VisitLambda<TFunc>(Expression<TFunc> node)
        {
            if (node.Parameters.Count == 1 && node.Parameters[0].Type == typeof(TEntity))
            {
                return Visit(node.Body);
            }

            return node;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType is not ExpressionType.Quote)
            {
                return node;
            }

            return Visit(node.Operand);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node is { Expression.Type: var type, Member: PropertyInfo property }
                && type == typeof(TEntity))
            {
                return Expression.Property(Parameter, property);
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is not ExpressionType.Equal)
            {
                return node;
            }

            if (ExpressionHelpers.IsNull(node.Right) && Visit(node.Left) is MemberExpression left)
            {
                return left;
            }

            if (ExpressionHelpers.IsNull(node.Left) && Visit(node.Right) is MemberExpression right)
            {
                return right;
            }

            return node;
        }
    }

    #endregion
}
