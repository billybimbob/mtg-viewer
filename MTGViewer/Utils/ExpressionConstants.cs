using System.Collections.Generic;
using System.Reflection;

namespace System.Linq.Expressions;

internal static class ExpressionConstants
{
    private static ConstantExpression? _null;
    public static ConstantExpression Null => _null ??= Expression.Constant(null);


    private static ConstantExpression? _zero;
    public static ConstantExpression Zero => _zero ??= Expression.Constant(0);


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



    private static MethodInfo? _enumLessThan;
    private static MethodInfo? _enumGreaterThan;


    private static bool EnumLessThan<TEnum>(TEnum left, TEnum right) where TEnum : Enum
    {
        return left.CompareTo(right) < 0;
    }

    private static bool EnumGreaterThan<TEnum>(TEnum left, TEnum right) where TEnum : Enum
    {
        return left.CompareTo(right) > 0;
    }


    public static MethodInfo EnumLessThan(Type enumType)
    {
        const BindingFlags staticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        _enumLessThan ??= typeof(ExpressionConstants)
            .GetMethod(nameof(ExpressionConstants.EnumLessThan), staticPrivate);

        return _enumLessThan!.MakeGenericMethod(enumType);
    }

    public static MethodInfo EnumGreaterThan(Type enumType)
    {
        const BindingFlags staticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        _enumGreaterThan ??= typeof(ExpressionConstants)
            .GetMethod(nameof(ExpressionConstants.EnumGreaterThan), staticPrivate);

        return _enumGreaterThan!.MakeGenericMethod(enumType);
    }
}