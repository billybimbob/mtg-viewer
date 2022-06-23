using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Utils;

internal static class ExpressionHelpers
{
    public static bool IsNull(Expression node)
        => node is ConstantExpression { Value: null };

    public static bool IsOrderBy(MethodCallExpression call)
        => DoesMethodEqual(
            call.Method,
            QueryableMethods.OrderBy,
            QueryableMethods.OrderByDescending);

    public static bool IsOrderedMethod(MethodCallExpression call)
        => DoesMethodEqual(
            call.Method,
            QueryableMethods.OrderBy,
            QueryableMethods.OrderByDescending,
            QueryableMethods.ThenBy,
            QueryableMethods.ThenByDescending);

    public static bool IsDescending(MethodCallExpression call)
        => DoesMethodEqual(
            call.Method,
            QueryableMethods.OrderByDescending,
            QueryableMethods.ThenByDescending);

    public static bool IsSeekBy(MethodCallExpression call)
        => DoesMethodEqual(
            call.Method,
            PagingExtensions.SeekByMethod);

    public static bool IsAfter(MethodCallExpression call)
        => DoesMethodEqual(
            call.Method,
            PagingExtensions.AfterReference,
            PagingExtensions.AfterKeyReference,
            PagingExtensions.AfterKeyValue);

    public static bool IsThenTake(MethodCallExpression call)
         => DoesMethodEqual(
            call.Method,
            PagingExtensions.ThenTakeMethod);

    public static bool IsToSeekList(MethodCallExpression call)
        => DoesMethodEqual(
            call.Method,
            PagingExtensions.ToSeekListMethodInfo);

    public static bool IsToSeekList(Expression expression)
        => expression is MethodCallExpression call
            && IsToSeekList(call);

    private static bool DoesMethodEqual(MethodInfo method, params MethodInfo[] options)
    {
        if (method is { IsGenericMethod: true, IsGenericMethodDefinition: false })
        {
            method = method.GetGenericMethodDefinition();
        }

        return options.Any(m => m == method);
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
