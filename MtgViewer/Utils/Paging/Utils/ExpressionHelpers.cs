using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Utils;

internal static class ExpressionHelpers
{
    public static bool IsNull(Expression node)
        => node is ConstantExpression { Value: null };

    public static bool IsOrderBy(MethodCallExpression call)
    {
        return call.Method.Name
            is nameof(Queryable.OrderBy)
                or nameof(Queryable.OrderByDescending);
    }

    public static bool IsOrderedMethod(MethodCallExpression call)
    {
        return call.Method.Name
            is nameof(Queryable.OrderBy)
                or nameof(Queryable.ThenBy)
                or nameof(Queryable.OrderByDescending)
                or nameof(Queryable.ThenByDescending);
    }

    public static bool IsDescending(MethodCallExpression call)
    {
        return call.Method.Name
            is nameof(Queryable.OrderByDescending)
                or nameof(Queryable.ThenByDescending);
    }

    public static IEnumerable<MemberExpression> GetLineage(MemberExpression? member)
    {
        while (member is not null)
        {
            yield return member;
            member = member.Expression as MemberExpression;
        }
    }

    public static string GetLineageName(MemberExpression member)
    {
        var memberNames = GetLineage(member)
            .Reverse()
            .Select(m => m.Member.Name);

        return string.Join(string.Empty, memberNames);
    }

    public static bool IsDescendant(MemberExpression? node, MemberExpression possibleAncestor)
    {
        if (node is null)
        {
            return false;
        }

        string nodeName = GetLineageName(node);
        string ancestor = GetLineageName(possibleAncestor);

        const StringComparison ordinal = StringComparison.Ordinal;

        return nodeName.StartsWith(ancestor, ordinal);
    }
}
