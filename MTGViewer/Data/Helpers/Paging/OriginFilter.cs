using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace System.Paging;


internal sealed class OriginFilter
{
    public static Expression<Func<TEntity, bool>> Create<TEntity>(
        IQueryable<TEntity> query,
        TEntity origin,
        SeekDirection direction)
    {
        return new OriginFilter<TEntity, TEntity>(query, origin, direction, null)
            .BuildExpression();
    }


    public static Expression<Func<TEntity, bool>> Create<TEntity, TOrigin>(
        IQueryable<TEntity> query,
        TOrigin origin,
        SeekDirection direction,
        Expression<Func<TEntity, TOrigin>> selector)
    {
        return new OriginFilter<TEntity, TOrigin>(query, origin, direction, selector)
            .BuildExpression();
    }
}


internal sealed class OriginFilter<TEntity, TOrigin>
{ 
    private readonly IQueryable<TEntity> _query;
    private readonly TOrigin _origin;
    private readonly SeekDirection _direction;
    private readonly MemberExpression? _selectOrigin;


    internal OriginFilter(
        IQueryable<TEntity> query, 
        TOrigin origin, 
        SeekDirection direction, 
        Expression<Func<TEntity, TOrigin>>? selector)
    {
        ArgumentNullException.ThrowIfNull(origin);

        _query = query;
        _origin = origin;
        _direction = direction;

        _selectOrigin = GetOriginSelector(selector);

        if (_selectOrigin is null && typeof(TEntity) != typeof(TOrigin))
        {
            throw new ArgumentException(nameof(selector));
        }
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


    private static ChainEquality? _chainEquality;
    private static IEqualityComparer<MemberExpression> CommonParent => _chainEquality ??= new();




    internal Expression<Func<TEntity, bool>> BuildExpression()
    {
        if (!OrderKeys.Any())
        {
            throw new InvalidOperationException("There are no properties to filter by");
        }

        var firstKey = OrderKeys.First();

        var otherKeys = OrderKeys
            .Select((key, i) => (key, i))
            .Skip(1);

        var filter = CompareTo(firstKey);

        foreach ((KeyOrder key, int before) in otherKeys)
        {
            filter = Expression.OrElse(
                filter, CompareTo(key, OrderKeys.Take(before)));
        }

        return Expression.Lambda<Func<TEntity, bool>>(filter, Parameter);
    }


    private static MemberExpression? GetOriginSelector(Expression<Func<TEntity, TOrigin>>? selector)
    {
        if (selector?.Body is MemberExpression s
            && OrderVisitor.Visit(s) is MemberExpression getOrigin)
        {
            return getOrigin;
        }

        return null;
    }


    private IEnumerable<KeyOrder> OrderProperties()
    {
        // get only top level expressions
        var source = _query.Expression;

        while (source is MethodCallExpression orderBy)
        {
            source = IsOrderBy(orderBy) ? null : orderBy.Arguments.ElementAtOrDefault(0);

            if (!IsOrderedMethod(orderBy)
                || orderBy.Arguments.ElementAtOrDefault(1) is not UnaryExpression quote
                || quote.Operand is not LambdaExpression lambda
                || lambda.Body is not MemberExpression)
            {
                continue;
            }

            if (OrderVisitor.Visit(lambda.Body) is MemberExpression propertyOrder
                && IsOriginProperty(propertyOrder))
            {
                var ordering = IsDescending(orderBy)
                    ? Ordering.Descending
                    : Ordering.Ascending;

                yield return new KeyOrder(propertyOrder, ordering);
            }
            else
            {
                // TODO: parse projection and use it as a property translation map
                throw new InvalidOperationException(
                    "Ordering method found, but cannot be used as a filter. Orderings must come after a projection");
            }
        }
    }


    private IEnumerable<NullCheck> NullProperties()
    {
        MemberExpression? lastProperty = null;

        var source = _query.Expression;

        while (source is MethodCallExpression orderBy)
        {
            source = IsOrderBy(orderBy) ? null : orderBy.Arguments.ElementAtOrDefault(0);

            if (!IsOrderedMethod(orderBy)
                || orderBy.Arguments.ElementAtOrDefault(1) is not UnaryExpression quote
                || quote.Operand is not LambdaExpression lambda)
            {
                continue;
            }

            if (lambda.Body is MemberExpression
                && OrderVisitor.Visit(lambda.Body) is MemberExpression propertyOrder)
            {
                lastProperty = propertyOrder;
                continue;
            }
            
            if (lambda.Body is not BinaryExpression
                || OrderVisitor.Visit(lambda.Body) is not MemberExpression nullOrder)
            {
                continue;
            }

            // a null is only used as a sort property if there are also 
            // non null orderings specified

            // null check ordering by itself it not unique enough

            if (IsOriginProperty(nullOrder) && IsDescendant(lastProperty, nullOrder))
            {
                var ordering = IsDescending(orderBy)
                    ? NullOrder.First
                    : NullOrder.Last;

                yield return new NullCheck(nullOrder, ordering);
            }
        }
    }


    private bool IsOriginProperty(MemberExpression entityProperty)
    {
        return _selectOrigin is null || IsDescendant(entityProperty, _selectOrigin);
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


    private ExpressionVisitor? _replaceOrigin;

    private Expression ReplaceWithOrigin(Expression node)
    {
        _replaceOrigin ??= new ReplaceEntity(_origin);

        return _replaceOrigin.Visit(node);
    }


    private IReadOnlyDictionary<MemberExpression, NullOrder>? _nullOrders;

    private NullOrder GetNullOrder(MemberExpression node)
    {
        _nullOrders ??= NullProperties()
            .ToDictionary(nc => nc.Key, nc => nc.Ordering, CommonParent);

        return _nullOrders.GetValueOrDefault(node);
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
            return Expression.GreaterThan(parameter, ReplaceWithOrigin(parameter));
        }

        if (parameter.Type == typeof(string))
        {
            return Expression.GreaterThan(
                Expression.Call(parameter, ExpressionConstants.StringCompare, ReplaceWithOrigin(parameter)),
                ExpressionConstants.Zero);
        }

        return nullOrder switch
        {
            NullOrder.First => OnlyOriginNull(parameter),
            NullOrder.Last => OnlyParameterNull(parameter),
            NullOrder.None or _ =>
                throw new ArgumentException(
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
            return Expression.LessThan(parameter, ReplaceWithOrigin(parameter));
        }

        if (parameter.Type == typeof(string))
        {
            return Expression.LessThan(
                Expression.Call(parameter, ExpressionConstants.StringCompare, ReplaceWithOrigin(parameter)),
                ExpressionConstants.Zero);
        }

        return nullOrder switch
        {
            NullOrder.First => OnlyParameterNull(parameter),
            NullOrder.Last => OnlyOriginNull(parameter),
            NullOrder.None or _ =>
                throw new ArgumentException(
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
            Expression.Equal(ReplaceWithOrigin(parameter), ExpressionConstants.Null));
    }


    private BinaryExpression OnlyParameterNull(MemberExpression parameter)
    {
        return Expression.AndAlso(
            Expression.Equal(parameter, ExpressionConstants.Null),
            Expression.NotEqual(ReplaceWithOrigin(parameter), ExpressionConstants.Null));
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
            ReplaceWithOrigin(parameter));

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
            Expression.NotEqual(ReplaceWithOrigin(parameter), ExpressionConstants.Null));

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
            Expression.Equal(ReplaceWithOrigin(parameter), ExpressionConstants.Null));

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


    private class ReplaceEntity : ExpressionVisitor
    {
        private readonly ConstantExpression _newEntity;

        public ReplaceEntity(TOrigin newEntity)
        {
            _newEntity = Expression.Constant(newEntity);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression?.Type == typeof(TOrigin)
                && node.Member is PropertyInfo property)
            {
                return Expression.Property(_newEntity, property);
            }

            return base.VisitMember(node);
        }
    }


    private sealed class ChainEquality : EqualityComparer<MemberExpression>
    {
        // recursive equality, keep an eye on perf

        public override bool Equals(MemberExpression? m1, MemberExpression? m2)
        {
            if (m1 is null && m2 is null)
            {
                return true;
            }

            if (m1 is null || m2 is null || !m1.Member.Equals(m2.Member))
            {
                return false;
            }

            if (m1.Expression is MemberExpression chain1
                && m2.Expression is MemberExpression chain2)
            {
                return Equals(chain1, chain2);
            }
            else
            {
                return m1.Expression == m2.Expression;
            }
        }

        public override int GetHashCode(MemberExpression node)
        {
            int hash = node.Member.GetHashCode();

            if (node.Expression is MemberExpression chain)
            {
                hash ^= GetHashCode(chain);
            }

            return hash;
        }
    }


    private class OrderByVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _parameter;

        public OrderByVisitor(ParameterExpression parameter)
        {
            _parameter = parameter;
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
                return base.VisitBinary(node);
            }

            if (IsNull(node.Right) && Visit(node.Left) is MemberExpression left)
            {
                return left;
            }

            if (IsNull(node.Left) && Visit(node.Right) is MemberExpression right)
            {
                return right;
            }

            return base.VisitBinary(node);
        }

        private bool IsNull(Expression node)
        {
            return node is ConstantExpression constant
                && constant.Value is null;
        }
    }

    #endregion


    #region Method helpers

    private static bool IsOrderBy(MethodCallExpression orderBy)
    {
        return orderBy.Method.Name 
            is nameof(Queryable.OrderBy)
                or nameof(Queryable.OrderByDescending);
    }

    private static bool IsOrderedMethod(MethodCallExpression orderBy)
    {
        return orderBy.Method.Name 
            is nameof(Queryable.OrderBy)
                or nameof(Queryable.ThenBy)
                or nameof(Queryable.OrderByDescending)
                or nameof(Queryable.ThenByDescending);
    }

    private static bool IsDescending(MethodCallExpression orderBy)
    {
        return orderBy.Method.Name
            is nameof(Queryable.OrderByDescending)
                or nameof(Queryable.ThenByDescending);
    }


    private static IEnumerable<MemberExpression> GetLineage(MemberExpression? member)
    {
        while (member is not null)
        {
            yield return member;
            member = member.Expression as MemberExpression;
        }
    }

    private static bool IsDescendant(MemberExpression? node, MemberExpression possibleAncestor)
    {
        return GetLineage(node)
            // could be a slow way to do this, possible On^2
            .Any(m => CommonParent.Equals(m, possibleAncestor));
    }

    private static bool IsComparable(Type type)
    {
        return type.IsAssignableTo(typeof(IComparable))
            || type.IsAssignableTo(typeof(IComparable<>).MakeGenericType(type));
    }

    #endregion
}
