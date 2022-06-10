using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal static class ExpressionHelpers
{
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

    public static PropertyInfo GetKeyProperty(Type entityType)
    {
        if (!IsEntityType(entityType))
        {
            throw new ArgumentException("The type is a value or generic type", nameof(entityType));
        }

        const string id = "Id";
        const BindingFlags binds = BindingFlags.Instance | BindingFlags.Public;

        string typeId = $"{entityType.Name}{id}";

        // could memo this in a static dict?
        PropertyInfo? key;

        var entityProperties = entityType.GetProperties(binds);

        key = entityProperties
            .FirstOrDefault(e => e.GetCustomAttribute<KeyAttribute>() is not null);

        if (key != default)
        {
            return key;
        }

        key = entityProperties
            .FirstOrDefault(e => e.Name == id || e.Name == typeId);

        if (key != default)
        {
            return key;
        }

        key = entityProperties.FirstOrDefault(e => e.Name.Contains(id));

        if (key != default)
        {
            return key;
        }

        var nestedType = entityProperties
            .Select(e => e.PropertyType)
            .FirstOrDefault(IsEntityType);

        if (nestedType != default)
        {
            return GetKeyProperty(nestedType);
        }

        throw new ArgumentException($"type {entityType.Name} is invalid");
    }

    private static bool IsEntityType(Type type)
        => !type.IsValueType && !type.IsGenericType;

    public static PropertyInfo GetKeyProperty<TEntity>() where TEntity : class
        => GetKeyProperty(typeof(TEntity));

    public static bool IsExecuteMethod(MethodInfo methodInfo)
    {
        if (!methodInfo.IsGenericMethodDefinition)
        {
            methodInfo = methodInfo.GetGenericMethodDefinition();
        }

        return methodInfo == QueryableMethods.AnyWithoutPredicate
            || methodInfo == QueryableMethods.AnyWithPredicate

            || methodInfo == QueryableMethods.All

            || methodInfo == QueryableMethods.CountWithoutPredicate
            || methodInfo == QueryableMethods.CountWithPredicate

            || methodInfo == QueryableMethods.LongCountWithoutPredicate
            || methodInfo == QueryableMethods.LongCountWithPredicate

            || methodInfo == QueryableMethods.FirstWithoutPredicate
            || methodInfo == QueryableMethods.FirstWithPredicate

            || methodInfo == QueryableMethods.FirstOrDefaultWithoutPredicate
            || methodInfo == QueryableMethods.FirstOrDefaultWithPredicate

            || methodInfo == QueryableMethods.LastWithoutPredicate
            || methodInfo == QueryableMethods.LastWithPredicate

            || methodInfo == QueryableMethods.LastOrDefaultWithoutPredicate
            || methodInfo == QueryableMethods.LastOrDefaultWithPredicate

            || methodInfo == QueryableMethods.SingleWithoutPredicate
            || methodInfo == QueryableMethods.SingleWithPredicate

            || methodInfo == QueryableMethods.SingleOrDefaultWithoutPredicate
            || methodInfo == QueryableMethods.SingleOrDefaultWithPredicate

            || methodInfo == QueryableMethods.MinWithoutSelector
            || methodInfo == QueryableMethods.MinWithSelector

            || methodInfo == QueryableMethods.MaxWithoutSelector
            || methodInfo == QueryableMethods.MaxWithSelector

            || QueryableMethods.IsSumWithoutSelector(methodInfo)
            || QueryableMethods.IsSumWithSelector(methodInfo)

            || QueryableMethods.IsAverageWithoutSelector(methodInfo)
            || QueryableMethods.IsAverageWithSelector(methodInfo)

            || methodInfo == QueryableMethods.Contains;
    }
}
