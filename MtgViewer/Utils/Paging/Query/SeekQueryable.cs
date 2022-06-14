using System;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal static class SeekQueryable
{
    public static ISeekQueryable<T> Create<T>(SeekProvider<T> provider, Expression expression)
        where T : class
    {
        return IsOrderedQuery(expression)
            ? new OrderedSeekQuery<T>(provider, expression)
            : new SeekQuery<T>(provider, expression);
    }

    public static IQueryable<T> Create<T>(IAsyncQueryProvider provider, Expression expression)
    {
        if (typeof(T).IsValueType)
        {
            throw new InvalidOperationException($"{typeof(T).Name} type is not a reference type");
        }

        object? seekQuery = IsOrderedQuery(expression)
            ? Activator.CreateInstance(
                typeof(OrderedSeekQuery<>).MakeGenericType(typeof(T)),
                new object[] { provider, expression })

            : Activator.CreateInstance(
                typeof(SeekQuery<>).MakeGenericType(typeof(T)),
                new object[] { provider, expression });

        return (IQueryable<T>)seekQuery!;
    }

    private static bool IsOrderedQuery(Expression query)
    {
        if (!query.Type.IsAssignableTo(typeof(IQueryable)))
        {
            throw new ArgumentException($"{query.Type.Name} is not {nameof(IQueryable)}", nameof(query));
        }

        return query.Type.IsAssignableTo(typeof(IOrderedQueryable));
    }
}
