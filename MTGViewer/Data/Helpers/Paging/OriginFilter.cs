using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;


internal static class OriginFilter
{
    public static Expression<Func<TEntity, bool>> Create<TEntity>(
        IQueryable<TEntity> query,
        TEntity origin,
        SeekDirection direction)
        where TEntity : notnull
    {
        var translator = new OriginTranslator(origin, null);

        return OriginFilter<TEntity>.Build(query, translator, direction);
    }


    public static Expression<Func<TEntity, bool>> Create<TEntity, TOrigin>(
        IQueryable<TEntity> query,
        TOrigin origin,
        SeekDirection direction,
        Expression<Func<TEntity, TOrigin>> selector)
        where TOrigin : notnull
    {
        var translator = new OriginTranslator(origin, selector);

        return OriginFilter<TEntity>.Build(query, translator, direction);
    }
}


internal sealed class OriginFilter<TEntity>
{ 
    internal static Expression<Func<TEntity, bool>> Build(
        IQueryable<TEntity> query, OriginTranslator origin, SeekDirection direction)
    {
        var builder = new OriginFilter<TEntity>(query, origin, direction);

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
            filter = Expression.OrElse(
                filter, builder.CompareTo(key, builder.OrderKeys.Take(before)));
        }

        return Expression.Lambda<Func<TEntity, bool>>(filter, Parameter);
    }


    private readonly IQueryable<TEntity> _query;
    private readonly OriginTranslator _origin;
    private readonly SeekDirection _direction;


    private OriginFilter(
        IQueryable<TEntity> query, OriginTranslator origin, SeekDirection direction)
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
                || !_origin.IsRegistered(nullOrder))
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


    private BinaryExpression CompareTo(KeyOrder keyOrder, IEnumerable<KeyOrder>? beforeKeys = null)
    {
        var (parameter, ordering) = keyOrder;

        var comparison = IsGreaterThan(ordering)
            ? GreaterThan(parameter)
            : LessThan(parameter);

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



    private BinaryExpression GreaterThan(MemberExpression parameter)
    {
        if (CheckParent(parameter) is not MemberExpression parent)
        {
            return GreaterThan(parameter, GetNullOrder(parameter));
        }

        return Expression.OrElse(
            GreaterThan(parent),
            Expression.AndAlso(
                NotNull(parent),
                GreaterThan(parameter, GetNullOrder(parameter))));
    }


    private BinaryExpression GreaterThan(MemberExpression parameter, NullOrder nullOrder)
    {
        if (parameter.Type.IsValueType && IsComparable(parameter.Type))
        {
            return Expression.GreaterThan(parameter, _origin.Translate(parameter));
        }

        if (parameter.Type == typeof(string))
        {
            return Expression.GreaterThan(
                Expression.Call(parameter, ExpressionConstants.StringCompare, _origin.Translate(parameter)),
                ExpressionConstants.Zero);
        }

        return nullOrder switch
        {
            NullOrder.First => OnlyOriginNull(parameter),
            NullOrder.Last => OnlyParameterNull(parameter),
            NullOrder.None or _ =>
                throw new InvalidOperationException(
                    $"{nameof(parameter)} with type {parameter.Type} cannot be compared")
        };
    }



    private BinaryExpression LessThan(MemberExpression parameter)
    {
        if (CheckParent(parameter) is not MemberExpression parent)
        {
            return LessThan(parameter, GetNullOrder(parameter));
        }

        return Expression.OrElse(
            LessThan(parent),
            Expression.AndAlso(
                NotNull(parent),
                LessThan(parameter, GetNullOrder(parameter))));
    }


    private BinaryExpression LessThan(MemberExpression parameter, NullOrder nullOrder)
    {
        if (parameter.Type.IsValueType && IsComparable(parameter.Type))
        {
            return Expression.LessThan(parameter, _origin.Translate(parameter));
        }

        if (parameter.Type == typeof(string))
        {
            return Expression.LessThan(
                Expression.Call(parameter, ExpressionConstants.StringCompare, _origin.Translate(parameter)),
                ExpressionConstants.Zero);
        }

        return nullOrder switch
        {
            NullOrder.First => OnlyParameterNull(parameter),
            NullOrder.Last => OnlyOriginNull(parameter),
            NullOrder.None or _ =>
                throw new InvalidOperationException(
                    $"{nameof(parameter)} with type {parameter.Type} cannot be compared")
        };
    }


    private MemberExpression? CheckParent(MemberExpression node)
    {
        return node.Expression
            is MemberExpression chain && GetNullOrder(chain) is not NullOrder.None
            ? chain : null;
    }


    private BinaryExpression OnlyOriginNull(MemberExpression parameter)
    {
        return Expression.AndAlso(
            Expression.NotEqual(parameter, ExpressionConstants.Null),
            Expression.Equal(_origin.Translate(parameter), ExpressionConstants.Null));
    }


    private BinaryExpression OnlyParameterNull(MemberExpression parameter)
    {
        return Expression.AndAlso(
            Expression.Equal(parameter, ExpressionConstants.Null),
            Expression.NotEqual(_origin.Translate(parameter), ExpressionConstants.Null));
    }



    private BinaryExpression? EqualTo(IEnumerable<MemberExpression> parameters)
    {
        if (parameters.FirstOrDefault() is not MemberExpression firstKey)
        {
            return null;
        }

        return parameters
            .Skip(1)
            .Select(EqualTo)
            .Aggregate(EqualTo(firstKey), Expression.AndAlso);
    }


    private BinaryExpression EqualTo(MemberExpression parameter)
    {
        var equalTo = Expression.Equal(
            parameter,
            _origin.Translate(parameter));

        if (CheckParent(parameter) is MemberExpression parent)
        {
            return Expression.OrElse(
                BothNull(parent),
                Expression.AndAlso(
                    NotNull(parent), equalTo));
        }

        return equalTo;
    }


    private BinaryExpression NotNull(MemberExpression parameter)
    {
        var bothNotNull = Expression.AndAlso(
            Expression.NotEqual(parameter, ExpressionConstants.Null),
            Expression.NotEqual(_origin.Translate(parameter), ExpressionConstants.Null));

        // only do first chain for perf, keep eye on

        if (CheckParent(parameter) is MemberExpression parent)
        {
            return Expression.AndAlso(
                NotNull(parent), bothNotNull);
        }

        return bothNotNull;
    }


    private BinaryExpression BothNull(MemberExpression parameter)
    {
        var bothNull = Expression.AndAlso(
            Expression.Equal(parameter, ExpressionConstants.Null),
            Expression.Equal(_origin.Translate(parameter), ExpressionConstants.Null));

        // only do first chain for perf, keep eye on

        if (CheckParent(parameter) is MemberExpression parent)
        {
            return Expression.OrElse(
                BothNull(parent), bothNull);
        }

        return bothNull;
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
