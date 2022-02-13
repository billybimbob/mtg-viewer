using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace System.Paging;


internal static class KeyFilter
{
    private record KeyOrder(MemberExpression Key, Ordering Ordering);

    private enum Ordering
    {
        Ascending,
        Descending
    }


    public static Expression<Func<T, bool>> BuildOriginFilter<T>(PageBuilder<T> pageBuilder)
    {
        if (pageBuilder.Origin is not T origin)
        {
            throw new InvalidOperationException("No origin is defined");
        }

        var parameter = Expression.Parameter(
            typeof(T), 
            typeof(T).Name[0].ToString().ToLower());

        var source = pageBuilder.Source.Expression;
        var direction = pageBuilder.Direction;

        var keys = GetOrderingKeys(source, parameter).ToList();

        if (!keys.Any())
        {
            throw new InvalidOperationException("There are no properties to filter by");
        }

        var filter = GetFilter(origin, direction, keys);

        return Expression.Lambda<Func<T, bool>>(filter, parameter);
    }


    private static IEnumerable<KeyOrder> GetOrderingKeys(
        Expression? source,
        ParameterExpression parameter)
    {
        return LastOrderProperties().Reverse();

        IEnumerable<KeyOrder> LastOrderProperties()
        {
            var replaceParameter = new ReplaceParameter(parameter);
            // get only top level expressions

            while (source is MethodCallExpression orderBy)
            {
                source = orderBy.Arguments.ElementAtOrDefault(0);

                bool isOrderMethod = orderBy.Method.Name switch
                {
                    nameof(Queryable.OrderBy)
                        or nameof(Queryable.ThenBy)
                        or nameof(Queryable.OrderByDescending)
                        or nameof(Queryable.ThenByDescending) => true,
                    _ => false
                };

                if (!isOrderMethod)
                {
                    continue;
                }

                if (orderBy.Arguments.ElementAtOrDefault(1) is not UnaryExpression quote
                    || quote.Operand is not LambdaExpression lambda
                    || lambda.Body is not MemberExpression orderMember
                    || orderMember.Member is not PropertyInfo
                    || replaceParameter.Visit(orderMember) is not MemberExpression paramOrder)
                {
                    continue;
                }

                var ordering = orderBy.Method.Name switch
                {
                    nameof(Queryable.OrderByDescending)
                        or nameof(Queryable.ThenByDescending) => Ordering.Descending,
                    _ => Ordering.Ascending
                };

                yield return new KeyOrder(paramOrder, ordering);

                bool isFirstOrder = orderBy.Method.Name switch
                {
                    nameof(Queryable.OrderBy)
                        or nameof(Queryable.OrderByDescending) => true,
                    _ => false
                };

                if (isFirstOrder)
                {
                    source = null;
                }
            }
        }
    }


    private static BinaryExpression GetFilter<T>(
        T originEntity,
        SeekDirection direction,
        IEnumerable<KeyOrder> keys)
    {
        var origin = new ReplaceEntity<T>(originEntity);

        var firstKey = keys.First();
        var otherKeys = keys
            .Select((key, i) => (key, i))
            .Skip(1);

        var filter = CompareTo(origin, direction, firstKey);

        foreach ((KeyOrder key, int before) in otherKeys)
        {
            filter = Expression.OrElse(
                filter,
                CompareTo(origin, direction, key, keys.Take(before)));
        }

        return filter;
    }


    private class ReplaceEntity<T> : ExpressionVisitor
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


    private class ReplaceParameter : ExpressionVisitor
    {
        private readonly ParameterExpression _parameter;

        public ReplaceParameter(ParameterExpression parameter)
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
    }


    private static BinaryExpression CompareTo<T>(
        ReplaceEntity<T> origin,
        SeekDirection direction,
        KeyOrder keyOrder,
        IEnumerable<KeyOrder>? beforeKeys = null)
    {
        var (key, ordering) = keyOrder;

        var equalKeys = beforeKeys
            ?.Select(k => k.Key)
            ?? Enumerable.Empty<MemberExpression>();

        return IsGreaterThan(direction, ordering)
            ? GreaterThan(origin, key, equalKeys)
            : LessThan(origin, key, equalKeys);
    }


    private static bool IsGreaterThan(SeekDirection direction, Ordering ordering)
    {
        return direction is SeekDirection.Forward && ordering is Ordering.Ascending
            || direction is SeekDirection.Backwards && ordering is Ordering.Descending;
    }


    private static BinaryExpression GreaterThan<T>(
        ReplaceEntity<T> origin,
        MemberExpression compareKey,
        IEnumerable<MemberExpression> equalKeys)
    {
        BinaryExpression greaterThan;

        if (compareKey.Expression is MemberExpression chainMember)
        {
            greaterThan = Expression.OrElse(
                GreaterThan(origin, chainMember, equalKeys),
                Expression.AndAlso(
                    ChainNotNull(origin, chainMember),
                    GreaterThan(origin, compareKey)));
        }
        else
        {
            greaterThan = GreaterThan(origin, compareKey);
        }

        var equalTo = EqualTo(origin, equalKeys);

        if (equalTo is null)
        {
            return greaterThan;
        }

        return Expression.AndAlso(equalTo, greaterThan);
    }


    private static BinaryExpression LessThan<T>(
        ReplaceEntity<T> origin,
        MemberExpression compareKey,
        IEnumerable<MemberExpression> equalKeys)
    {
        BinaryExpression lessThan;

        if (compareKey.Expression is MemberExpression chainMember)
        {
            lessThan = Expression.OrElse(
                LessThan(origin, chainMember, equalKeys),
                Expression.AndAlso(
                    ChainNotNull(origin, chainMember),
                    LessThan(origin, compareKey)));
        }
        else
        {
            lessThan = LessThan(origin, compareKey);
        }

        var equalTo = EqualTo(origin, equalKeys);

        if (equalTo is not null)
        {
            return Expression.AndAlso(equalTo, lessThan);
        }

        return lessThan;
    }


    private static BinaryExpression? EqualTo<T>(ReplaceEntity<T> origin, IEnumerable<MemberExpression> equalKeys)
    {
        if (equalKeys.FirstOrDefault() is not MemberExpression firstKey)
        {
            return null;
        }

        return equalKeys
            .Skip(1)
            .Select(k => EqualTo(origin, k))
            .Aggregate(
                EqualTo(origin, firstKey),
                Expression.AndAlso);
    }


    private static BinaryExpression LessThan<T>(ReplaceEntity<T> origin, MemberExpression paramKey)
    {
        if (paramKey.Type.IsValueType
            && (paramKey.Type.IsAssignableTo(typeof(IComparable))
                || paramKey.Type.IsAssignableTo(typeof(IComparable<>).MakeGenericType(paramKey.Type))))
        {
            return Expression.LessThan(paramKey, origin.Visit(paramKey));
        }

        if (typeof(string) is var stringType && paramKey.Type == stringType)
        {
            var compareTo = stringType
                .GetMethod(nameof(string.CompareTo), new[] { stringType });

            var stringCompare = Expression.Call(
                paramKey, compareTo!, origin.Visit(paramKey));

            return Expression.LessThan(stringCompare, Expression.Constant(0));
        }

        // null is less than not null

        var nullExpression = Expression.Constant(null);

        var paramIsNull = Expression.Equal(paramKey, nullExpression);
        var originNotNull = Expression.NotEqual(origin.Visit(paramKey), nullExpression);

        return Expression.AndAlso(paramIsNull, originNotNull);
    }


    private static BinaryExpression GreaterThan<T>(ReplaceEntity<T> origin, MemberExpression paramKey)
    {
        if (paramKey.Type.IsValueType
            && (paramKey.Type.IsAssignableTo(typeof(IComparable))
                || paramKey.Type.IsAssignableTo(typeof(IComparable<>).MakeGenericType(paramKey.Type))))
        {
            return Expression.GreaterThan(paramKey, origin.Visit(paramKey));
        }

        if (typeof(string) is var stringType && paramKey.Type == stringType)
        {
            var compareTo = stringType
                .GetMethod(nameof(string.CompareTo), new[] { stringType });

            var stringCompare = Expression.Call(
                paramKey, compareTo!, origin.Visit(paramKey));

            return Expression.GreaterThan(stringCompare, Expression.Constant(0));
        }

        // not null is greater than null

        var nullExpression = Expression.Constant(null);

        var paramNotNull = Expression.NotEqual(paramKey, nullExpression);
        var originIsNull = Expression.Equal(origin.Visit(paramKey), nullExpression);

        return Expression.AndAlso(paramNotNull, originIsNull);
    }


    private static BinaryExpression EqualTo<T>(ReplaceEntity<T> origin, MemberExpression paramKey)
    {
        var equalTo = Expression.Equal(paramKey, origin.Visit(paramKey));

        if (paramKey.Expression is MemberExpression chainMember)
        {
            return Expression.OrElse(
                ChainBothNull(origin, chainMember),
                Expression.AndAlso(
                    ChainNotNull(origin, chainMember), equalTo));
        }

        return equalTo;
    }


    private static BinaryExpression ChainNotNull<T>(ReplaceEntity<T> origin, MemberExpression chainKey)
    {
        var nullExpression = Expression.Constant(null);

        var bothNotNull = Expression.AndAlso(
            Expression.NotEqual(chainKey, nullExpression),
            Expression.NotEqual(origin.Visit(chainKey), nullExpression));

        // only do first chain for perf, keep eye on

        // if (chainKey.Expression is MemberExpression chainMember)
        // {
        //     return Expression.AndAlso(
        //         ChainNotNull(origin, chainMember), bothNotNull);
        // }

        return bothNotNull;
    }


    private static BinaryExpression ChainBothNull<T>(ReplaceEntity<T> origin, MemberExpression chainKey)
    {
        var nullExpression = Expression.Constant(null);

        var bothNull = Expression.AndAlso(
            Expression.Equal(chainKey, nullExpression),
            Expression.Equal(origin.Visit(chainKey), nullExpression));

        // only do first chain for perf, keep eye on

        // if (chainKey.Expression is MemberExpression chainMember)
        // {
        //     return Expression.OrElse(
        //         ChainBothNull(origin, chainMember), bothNull);
        // }

        return bothNull;
    }
}