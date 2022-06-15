using System;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query;

internal static class SeekQueryable
{
    public static ISeekQueryable<T> Create<T>(SeekProvider<T> provider, Expression expression)
    {
        return IsOrderedQuery(expression)
            ? new OrderedSeekQuery<T>(provider, expression)
            : new SeekQuery<T>(provider, expression);
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
