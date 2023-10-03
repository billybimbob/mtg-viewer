using System;
using System.Reflection;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal static class TypeHelpers
{
    public static readonly MethodInfo StringCompareTo
        = typeof(string).GetMethod(nameof(string.CompareTo), new[] { typeof(string) })!;

    public static readonly MethodInfo EnumLessThan
        = typeof(TypeHelpers).GetMethod(nameof(LessThan))!;

    public static readonly MethodInfo EnumGreaterThan
        = typeof(TypeHelpers).GetMethod(nameof(GreaterThan))!;

    public static bool LessThan<TEnum>(TEnum left, TEnum right) where TEnum : struct, Enum
        => left.CompareTo(right) < 0;

    public static bool GreaterThan<TEnum>(TEnum left, TEnum right) where TEnum : struct, Enum
        => left.CompareTo(right) > 0;

    public static bool IsScalarType(Type type)
        => type.IsEnum
            || IsValueComparable(type)
            || type == typeof(string);

    public static bool IsValueComparable(Type type)
    {
        if (type is { IsValueType: true }
            && type.IsAssignableTo(typeof(IComparable<>).MakeGenericType(type)))
        {
            return true;
        }

        if (Nullable.GetUnderlyingType(type) is Type inner
            && IsValueComparable(inner))
        {
            return true;
        }

        return false;
    }
}
