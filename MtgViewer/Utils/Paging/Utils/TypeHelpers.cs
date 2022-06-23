using System;
using System.Reflection;

namespace EntityFrameworkCore.Paging.Utils;

internal static class TypeHelpers
{
    public static readonly MethodInfo StringCompareTo
        = typeof(string)
            .GetMethod(nameof(string.CompareTo), new[] { typeof(string) })!;

    public static bool IsScalarType(Type type)
        => type.IsEnum
            || IsValueComparable(type)
            || type == typeof(string);

    public static bool IsValueComparable(Type type)
        => (type is { IsValueType: true }
            && type.IsAssignableTo(typeof(IComparable<>).MakeGenericType(type)))

            || (Nullable.GetUnderlyingType(type) is Type inner
                && IsValueComparable(inner));

    public static bool HasReferenceConstraint(Type? type)
        => ((type?.GenericParameterAttributes ?? 0)
            & GenericParameterAttributes.ReferenceTypeConstraint) != 0;

    public static bool HasValueConstraint(Type? type)
        => ((type?.GenericParameterAttributes ?? 0)
            & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;

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
}
