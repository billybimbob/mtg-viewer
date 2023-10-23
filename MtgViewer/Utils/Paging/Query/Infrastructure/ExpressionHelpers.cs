using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal static class ExpressionHelpers
{
    public static bool IsNull(Expression node)
        => node is ConstantExpression { Value: null };

    public static bool IsOrderBy(MethodCallExpression call)
        => DoesMethodEqual(
            call.Method,
            QueryableMethods.OrderBy,
            QueryableMethods.OrderByDescending);

    public static bool IsThenBy(MethodInfo methodInfo)
        => DoesMethodEqual(
            methodInfo,
            QueryableMethods.ThenBy,
            QueryableMethods.ThenByDescending);

    public static bool IsThenBy(MethodCallExpression call)
        => IsThenBy(call.Method);

    public static bool IsOrderedMethod(MethodCallExpression call)
        => DoesMethodEqual(
            call.Method,
            QueryableMethods.OrderBy,
            QueryableMethods.OrderByDescending,
            QueryableMethods.ThenBy,
            QueryableMethods.ThenByDescending);

    public static bool IsDescending(MethodInfo methodInfo)
        => DoesMethodEqual(
            methodInfo,
            QueryableMethods.OrderByDescending,
            QueryableMethods.ThenByDescending);

    public static bool IsDescending(MethodCallExpression call)
        => IsDescending(call.Method);

    public static bool IsTake(MethodCallExpression call)
        => DoesMethodEqual(
            call.Method,
            QueryableMethods.Take);

    public static bool IsSeekQuery(MethodCallExpression call)
        => DoesMethodEqual(
            call.Method,
            PagingMethods.SeekBy,
            PagingMethods.AfterReference,
            PagingMethods.AfterPredicate);

    public static bool IsSeekBy(MethodCallExpression call)
        => DoesMethodEqual(call.Method, PagingMethods.SeekBy);

    public static bool IsAfter(MethodCallExpression call)
        => DoesMethodEqual(
            call.Method,
            PagingMethods.AfterReference,
            PagingMethods.AfterPredicate);

    public static bool IsToSeekList(MethodCallExpression call)
        => DoesMethodEqual(
            call.Method,
            PagingMethods.ToSeekList);

    public static bool IsToSeekList(Expression expression)
        => expression is MethodCallExpression call && IsToSeekList(call);

    public static Type? FindSeekListEntity(Expression expression)
    {
        if (expression is MethodCallExpression call
            && IsToSeekList(call))
        {
            return call.Type.GenericTypeArguments[0];
        }

        return null;
    }

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
        var overlapChain = GetLineage(member)
            .Reverse()
            .Select(m => m.Member.Name);

        return string.Join('.', overlapChain);
    }

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
