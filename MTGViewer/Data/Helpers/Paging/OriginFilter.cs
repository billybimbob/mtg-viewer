using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace System.Paging;


internal sealed class OriginFilter
{
    public static Expression<Func<T, bool>> FromPageBuilder<T>(PageBuilder<T> pageBuilder)
    {
        return new OriginFilter<T>(pageBuilder).BuildExpression();
    }
}


internal sealed class OriginFilter<T>
{ 
    internal OriginFilter(PageBuilder<T> pageBuilder)
    {
        if (pageBuilder.Origin is null)
        {
            throw new ArgumentException($"{nameof(pageBuilder.Origin)} is null");
        }

        _pageBuilder = pageBuilder;

        _orderKeys = OrderProperties()
            .Reverse()
            .ToList();

        if (!_orderKeys.Any())
        {
            throw new InvalidOperationException("There are no properties to filter by");
        }
    }

    private readonly PageBuilder<T> _pageBuilder;
    private readonly IReadOnlyList<KeyOrder> _orderKeys;

    private static MethodInfo? _stringContains;

    private static ConstantExpression? _null;
    private static ConstantExpression? _zero;
    private static ParameterExpression? _parameter;

    public static IEqualityComparer<MemberExpression>? _parentEquality;

    public static ExpressionVisitor? _orderVisitor;
    private ExpressionVisitor? _origin;

    private IReadOnlyDictionary<MemberExpression, NullOrder>? _nullOrders;


    private static MethodInfo StringCompare =>
        _stringContains ??= typeof(string)
            .GetMethod(nameof(string.CompareTo), new[]{ typeof(string) })!;


    private static ConstantExpression Null => _null ??= Expression.Constant(null);

    private static ConstantExpression Zero => _zero ??= Expression.Constant(0);

    private static ParameterExpression Parameter =>
        _parameter ??= Expression.Parameter(
            typeof(T), typeof(T).Name[0].ToString().ToLower());


    private static ExpressionVisitor OrderVisitor =>
        _orderVisitor ??= new OrderByVisitor(Parameter);

    private static IEqualityComparer<MemberExpression> ParentEquality =>
        _parentEquality ??= new ChainEquality();


    internal Expression<Func<T, bool>> BuildExpression()
    {
        var firstKey = _orderKeys.First();

        var otherKeys = _orderKeys
            .Select((key, i) => (key, i))
            .Skip(1);

        var filter = CompareTo(firstKey);

        foreach ((KeyOrder key, int before) in otherKeys)
        {
            filter = Expression.OrElse(
                filter, CompareTo(key, _orderKeys.Take(before)));
        }

        return Expression.Lambda<Func<T, bool>>(filter, Parameter);
    }


    private IEnumerable<KeyOrder> OrderProperties()
    {
        // get only top level expressions
        var source = _pageBuilder.Source.Expression;

        while (source is MethodCallExpression orderBy)
        {
            if (IsOrderedMethod(orderBy)
                && orderBy.Arguments.ElementAtOrDefault(1) is UnaryExpression quote
                && quote.Operand is LambdaExpression lambda
                && lambda.Body is MemberExpression
                && OrderVisitor.Visit(lambda.Body) is MemberExpression propertyOrder)
            {
                var ordering = IsDescending(orderBy)
                    ? Ordering.Descending
                    : Ordering.Ascending;

                yield return new KeyOrder(propertyOrder, ordering);
            }

            source = IsOrderBy(orderBy) ? null : orderBy.Arguments.ElementAtOrDefault(0);
        }
    }


    private IEnumerable<NullCheck> NullProperties()
    {
        MemberExpression? lastProperty = null;

        var source = _pageBuilder.Source.Expression;

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

            if (MemberChain(lastProperty)
                .Any(m => ParentEquality.Equals(m, nullOrder)))
            {
                var ordering = IsDescending(orderBy)
                    ? NullOrder.First
                    : NullOrder.Last;

                yield return new NullCheck(nullOrder, ordering);
            }
        }
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
        var dir = _pageBuilder.Direction;

        return dir is SeekDirection.Forward && ordering is Ordering.Ascending
            || dir is SeekDirection.Backwards && ordering is Ordering.Descending;
    }


    private Expression ReplaceWithOrigin(Expression node)
    {
        _origin ??= new ReplaceEntity(_pageBuilder.Origin!);

        return _origin.Visit(node);
    }


    private NullOrder GetNullOrder(MemberExpression node)
    {
        _nullOrders ??= NullProperties()
            .ToDictionary(nc => nc.Key, nc => nc.Ordering, ParentEquality);

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
                Expression.Call(parameter, StringCompare, ReplaceWithOrigin(parameter)),
                Zero);
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
                Expression.Call(parameter, StringCompare, ReplaceWithOrigin(parameter)),
                Zero);
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
            Expression.NotEqual(parameter, Null),
            Expression.Equal(ReplaceWithOrigin(parameter), Null));
    }


    private BinaryExpression OnlyParameterNull(MemberExpression parameter)
    {
        return Expression.AndAlso(
            Expression.Equal(parameter, Null),
            Expression.NotEqual(ReplaceWithOrigin(parameter), Null));
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
            Expression.NotEqual(parameter, Null),
            Expression.NotEqual(ReplaceWithOrigin(parameter), Null));

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
            Expression.Equal(parameter, Null),
            Expression.Equal(ReplaceWithOrigin(parameter), Null));

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

        public ReplaceEntity(T newEntity)
        {
            _newEntity = Expression.Constant(newEntity);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression?.Type == typeof(T)
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

            return m1.Expression == m2.Expression;
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


    private static IEnumerable<MemberExpression> MemberChain(MemberExpression? member)
    {
        while (member is not null)
        {
            yield return member;
            member = member.Expression as MemberExpression;
        }
    }

    private static bool IsComparable(Type type)
    {
        return type.IsAssignableTo(typeof(IComparable))
            || type.IsAssignableTo(typeof(IComparable<>).MakeGenericType(type));
    }

    #endregion
}
