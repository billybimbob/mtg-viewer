using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query;

internal static class ExpressionHelpers
{
    private static ConstantExpression? _zero;
    public static ConstantExpression Zero => _zero ??= Expression.Constant(0);

    private static ConstantExpression? _null;
    public static ConstantExpression Null => _null ??= Expression.Constant(null);

    public static bool IsNull(Expression node)
        => node is ConstantExpression { Value: null };

    public static bool IsOrderBy(MethodCallExpression orderBy)
    {
        return orderBy.Method.Name
            is nameof(Queryable.OrderBy)
                or nameof(Queryable.OrderByDescending);
    }

    public static bool IsOrderedMethod(MethodCallExpression orderBy)
    {
        return orderBy.Method.Name
            is nameof(Queryable.OrderBy)
                or nameof(Queryable.ThenBy)
                or nameof(Queryable.OrderByDescending)
                or nameof(Queryable.ThenByDescending);
    }

    public static bool IsDescending(MethodCallExpression orderBy)
    {
        return orderBy.Method.Name
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
