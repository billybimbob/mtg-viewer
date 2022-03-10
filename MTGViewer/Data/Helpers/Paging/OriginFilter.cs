using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;


internal static class OriginFilter
{
    public static Expression<Func<TEntity, bool>> Build<TEntity>(
        IQueryable<TEntity> query,
        TEntity origin,
        SeekDirection direction)
        where TEntity : notnull
    {
        var translator = new OriginTranslator<TEntity, TEntity>(origin, null);

        return OriginFilter<TEntity, TEntity>.Build(query, translator, direction);
    }


    public static Expression<Func<TEntity, bool>> Build<TEntity, TOrigin>(
        IQueryable<TEntity> query,
        TOrigin origin,
        SeekDirection direction,
        Expression<Func<TEntity, TOrigin>> selector)
        where TOrigin : notnull
    {
        var translator = new OriginTranslator<TOrigin, TEntity>(origin, selector);

        return OriginFilter<TOrigin, TEntity>.Build(query, translator, direction);
    }
}


internal sealed class OriginFilter<TOrigin, TEntity>
{ 
    internal static Expression<Func<TEntity, bool>> Build(
        IQueryable<TEntity> query,
        OriginTranslator<TOrigin, TEntity> origin,
        SeekDirection direction)
    {
        var builder = new OriginFilter<TOrigin, TEntity>(query, origin, direction);

        if (!builder.OrderKeys.Any())
        {
            throw new InvalidOperationException("There are no properties to filter by");
        }

        var firstKey = builder.OrderKeys.First();

        var otherKeys = builder.OrderKeys
            .Select((key, i) => (key, i))
            .Skip(1);

        var filter = builder.CompareTo(firstKey);

        foreach ((KeyOrder key, int before) in otherKeys)
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
            throw new InvalidOperationException(
                "The given origin and orderings could not be compared");
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


    private List<KeyOrder>? _orderKeys;
    private IReadOnlyList<KeyOrder> OrderKeys =>
        _orderKeys ??= OrderProperties().Reverse().ToList();


    private static ParameterExpression? _parameter;
    private static ParameterExpression Parameter =>
        _parameter ??=
            Expression.Parameter(
                typeof(TEntity), 
                typeof(TEntity).Name[0].ToString().ToLower());


    private static OrderByVisitor? _orderByVisitor;
    private static ExpressionVisitor OrderVisitor => _orderByVisitor ??= new(Parameter);


    private IEnumerable<KeyOrder> OrderProperties()
    {
        // get only top level expressions
        var source = _query.Expression;

        while (source is MethodCallExpression orderBy)
        {
            source = ExpressionHelpers.IsOrderBy(orderBy) ? null : orderBy.Arguments.ElementAtOrDefault(0);

            if (OrderVisitor.Visit(orderBy) is MemberExpression propertyOrder
                && _origin.TryRegister(propertyOrder))
            {
                var ordering = ExpressionHelpers.IsDescending(orderBy)
                    ? Ordering.Descending
                    : Ordering.Ascending;

                yield return new KeyOrder(propertyOrder, ordering);
            }

            // TODO: parse projection and use it as a property translation map
        }
    }


    private IEnumerable<NullCheck> NullProperties()
    {
        MemberExpression? lastProperty = null;

        var source = _query.Expression;

        while (source is MethodCallExpression orderBy)
        {
            source = ExpressionHelpers.IsOrderBy(orderBy) ? null : orderBy.Arguments.ElementAtOrDefault(0);

            if (OrderVisitor.Visit(orderBy) is not MemberExpression nullOrder
                || !_origin.TryRegister(nullOrder))
            {
                continue;
            }

            // a null is only used as a sort property if there are also 
            // non null orderings specified

            // null check ordering by itself it not unique enough

            if (ExpressionHelpers.IsDescendant(lastProperty, nullOrder))
            {
                var ordering = ExpressionHelpers.IsDescending(orderBy)
                    ? NullOrder.First
                    : NullOrder.Last;

                yield return new NullCheck(nullOrder, ordering);
            }
            else
            {
                lastProperty = nullOrder;
            }
        }
    }


    private IReadOnlyDictionary<Expression, NullOrder>? _nullOrders;

    private NullOrder GetNullOrder(MemberExpression node)
    {
        _nullOrders ??= NullProperties()
            .ToDictionary(
                nc => (Expression)nc.Key,
                nc => nc.Ordering,
                ExpressionEqualityComparer.Instance);

        return _nullOrders.GetValueOrDefault(node);
    }


    private BinaryExpression? CompareTo(KeyOrder keyOrder, IEnumerable<KeyOrder>? beforeKeys = null)
    {
        var (parameter, ordering) = keyOrder;

        var comparison = IsGreaterThan(ordering)
            ? GreaterThan(parameter)
            : LessThan(parameter);

        if (comparison is null)
        {
            return null;
        }

        var equalKeys = beforeKeys
            ?.Select(k => k.Key)
            ?? Enumerable.Empty<MemberExpression>();

        if (EqualTo(equalKeys) is Expression equalTo)
        {
            return Expression.AndAlso(equalTo, comparison);
        }

        return comparison;
    }
    

    private bool IsGreaterThan(Ordering ordering)
    {
        return _direction is SeekDirection.Forward && ordering is Ordering.Ascending
            || _direction is SeekDirection.Backwards && ordering is Ordering.Descending;
    }



    private BinaryExpression? GreaterThan(MemberExpression parameter)
    {
        var origin = _origin.Translate(parameter);

        var greaterThan = (parameter.Type, GetNullOrder(parameter)) switch
        {
            _ when _origin.IsChainNull(parameter) => null,

            (Type t, _) when t is { IsValueType: true } && IsComparable(t) =>
                Expression.GreaterThan(parameter, origin),

            (Type t, _) when t == typeof(string) =>
                Expression.GreaterThan(
                    Expression.Call(parameter, ExpressionConstants.StringCompare, origin),
                    ExpressionConstants.Zero),

            (_, NullOrder.First) => OnlyOriginNull(parameter),
            (_, NullOrder.Last) => OnlyParameterNull(parameter),
            (_, NullOrder.None) or _ => null
        };

        if (CheckParent(parameter) is not MemberExpression parent)
        {
            return greaterThan;
        }

        return (GreaterThan(parent), greaterThan) switch
        {
            (BinaryExpression parentGreater, null) => parentGreater,

            (Expression parentGreater, not null) =>
                Expression.OrElse(parentGreater, greaterThan),

            (_, not null) => greaterThan,
            _ => null
        };
    }


    private BinaryExpression? LessThan(MemberExpression parameter)
    {
        var origin = _origin.Translate(parameter);

        var lessThan = (parameter.Type, GetNullOrder(parameter)) switch
        {
            _ when _origin.IsChainNull(parameter) => null,

            (Type t, _) when t is { IsValueType: true } && IsComparable(t) =>
                Expression.LessThan(parameter, origin),

            (Type t, _) when t == typeof(string) => 
                Expression.LessThan(
                    Expression.Call(parameter, ExpressionConstants.StringCompare, origin),
                    ExpressionConstants.Zero),

            (_, NullOrder.First) => OnlyParameterNull(parameter),
            (_, NullOrder.Last) => OnlyOriginNull(parameter),
            (_, NullOrder.None) or _ => null
        };

        if (CheckParent(parameter) is not MemberExpression parent)
        {
            return lessThan;
        }

        return (LessThan(parent), lessThan) switch
        {
            (BinaryExpression parentLess, null) => parentLess,

            (Expression parentLess, not null) =>
                Expression.OrElse(parentLess, lessThan),

            (_, not null) => lessThan,
            _ => null
        };
    }


    private MemberExpression? CheckParent(MemberExpression node)
    {
        return node.Expression is MemberExpression chain
            && GetNullOrder(chain) is not NullOrder.None
            ? chain
            : null;
    }


    private BinaryExpression? OnlyOriginNull(MemberExpression parameter)
    {
        if (!_origin.IsChainNull(parameter))
        {
            return null;
        }

        return Expression.NotEqual(parameter, ExpressionConstants.Null);
    }


    private BinaryExpression? OnlyParameterNull(MemberExpression parameter)
    {
        if (_origin.IsChainNull(parameter))
        {
            return null;
        }

        return Expression.Equal(parameter, ExpressionConstants.Null);
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
        var origin = _origin.Translate(parameter);
        bool sameType = parameter.Type == origin.Type;

        if (!sameType && GetNullOrder(parameter) is NullOrder.None)
        {
            return null;
        }

        return (sameType, BothIsNull(parameter), BothNotNull(parameter)) switch
        {
            (true, Expression isNull, Expression notNull) =>
                Expression.OrElse(
                    isNull,
                    Expression.AndAlso(
                        notNull,
                        Expression.Equal(parameter, origin))),

            (true, _, Expression notNull) =>
                Expression.AndAlso(
                    notNull,
                    Expression.Equal(parameter, origin)),

            (true, _, _) => Expression.Equal(parameter, origin),

            (_, Expression isNull, Expression notNull) =>
                Expression.OrElse(isNull, notNull),

            (_, BinaryExpression isNull, _) => isNull,

            (_, _, BinaryExpression notNull) => notNull,

            _ => null
        };
    }


    private BinaryExpression? BothNotNull(MemberExpression parameter)
    {
        if (_origin.IsNonNull(parameter))
        {
            return null;
        }

        if (_origin.IsChainNull(parameter))
        {
            return null;
        }

        return Expression.NotEqual(parameter, ExpressionConstants.Null);
    }


    private BinaryExpression? BothIsNull(MemberExpression parameter)
    {
        if (_origin.IsNonNull(parameter))
        {
            return null;
        }

        if (!_origin.IsChainNull(parameter))
        {
            return null;
        }

        return Expression.Equal(parameter, ExpressionConstants.Null);
    }



    #region Key Builder types

    private readonly record struct KeyOrder(MemberExpression Key, Ordering Ordering);

    private enum Ordering
    {
        Ascending,
        Descending,
    }

    private readonly record struct NullCheck(MemberExpression Key, NullOrder Ordering);

    private enum NullOrder
    {
        None,
        First,
        Last
    }


    private class OrderByVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _parameter;

        public OrderByVisitor(ParameterExpression parameter)
        {
            _parameter = parameter;
        }

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
            if (node.Parameters.Count == 1 && node.Parameters[0].Type == _parameter.Type)
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
            if (node.Expression?.Type == _parameter.Type
                && node.Member is PropertyInfo property)
            {
                return Expression.Property(_parameter, property);
            }

            return base.VisitMember(node);
        }
        
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is not ExpressionType.Equal)
            {
                return node;
            }

            if (IsNull(node.Right) && Visit(node.Left) is MemberExpression left)
            {
                return left;
            }

            if (IsNull(node.Left) && Visit(node.Right) is MemberExpression right)
            {
                return right;
            }

            return node;
        }

        private bool IsNull(Expression node)
        {
            return node is ConstantExpression constant
                && constant.Value is null;
        }
    }

    #endregion


    private static bool IsComparable(Type type)
    {
        return type.IsAssignableTo(typeof(IComparable))
            || type.IsAssignableTo(typeof(IComparable<>).MakeGenericType(type));
    }
}
