using System;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal static class QueryTypeHelpers
{
    public static Type? FindElementType(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var queryType = expression.Type;

        if (!queryType.IsAssignableTo(typeof(IQueryable)))
        {
            return null;
        }

        if (queryType.IsGenericTypeDefinition)
        {
            return null;
        }

        foreach (var typeArg in queryType.GenericTypeArguments)
        {
            if (queryType.IsAssignableTo(typeof(IQueryable<>).MakeGenericType(typeArg)))
            {
                return typeArg;
            }
        }

        return null;
    }
}
