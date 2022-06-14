using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal static class TypeHelpers
{
    private static MethodInfo? _stringContains;
    public static MethodInfo StringContains =>
        _stringContains ??= typeof(string)
            .GetMethod(nameof(string.Contains), new[] { typeof(string) })!;

    private static MethodInfo? _stringCompare;
    public static MethodInfo StringCompareTo =>
        _stringCompare ??= typeof(string)
            .GetMethod(nameof(string.CompareTo), new[] { typeof(string) })!;

    private static MethodInfo? _enumerableAll;
    public static MethodInfo All =>
        _enumerableAll ??= new Func<IEnumerable<object>, Func<object, bool>, bool>(Enumerable.All)
            .Method
            .GetGenericMethodDefinition();

    #region Enum Comparison

    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    private static readonly MethodInfo _enumLessThan
        = typeof(TypeHelpers).GetMethod(nameof(EnumLessThan), StaticPrivate)!;

    private static readonly MethodInfo _enumGreaterThan
        = typeof(TypeHelpers).GetMethod(nameof(EnumGreaterThan), StaticPrivate)!;

    private static bool EnumLessThan<TEnum>(TEnum left, TEnum right) where TEnum : Enum
        => left.CompareTo(right) < 0;

    private static bool EnumGreaterThan<TEnum>(TEnum left, TEnum right) where TEnum : Enum
        => left.CompareTo(right) > 0;

    public static MethodInfo EnumLessThan(Type enumType)
        => _enumLessThan.MakeGenericMethod(enumType);

    public static MethodInfo EnumGreaterThan(Type enumType)
        => _enumGreaterThan.MakeGenericMethod(enumType);

    #endregion

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
        => type is { IsValueType: false, IsGenericType: false };

    public static bool IsScalarType(Type type)
        => type.IsEnum
            || IsValueComparable(type)
            || type == typeof(string);

    public static bool IsValueComparable(Type type)
        => (type is { IsValueType: true }
            && type.IsAssignableTo(typeof(IComparable<>).MakeGenericType(type)))

            || (Nullable.GetUnderlyingType(type) is Type inner
                && IsValueComparable(inner));

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
