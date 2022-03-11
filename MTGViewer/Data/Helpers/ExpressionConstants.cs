using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace System.Linq.Expressions;

public static class ExpressionConstants
{
    private static MethodInfo? _stringContains;
    public static MethodInfo StringContains =>
        _stringContains ??= typeof(string)
            .GetMethod(nameof(string.Contains), new[] { typeof(string) })!;


    private static MethodInfo? _stringCompare;
    public static MethodInfo StringCompare =>
        _stringCompare ??= typeof(string)
            .GetMethod(nameof(string.CompareTo), new[]{ typeof(string) })!;


    private static MethodInfo? _enumerableAll;
    public static MethodInfo All =>
        _enumerableAll ??= new Func<IEnumerable<object>, Func<object, bool>, bool>(Enumerable.All)
            .Method
            .GetGenericMethodDefinition();


    private static ConstantExpression? _null;
    public static ConstantExpression Null => _null ??= Expression.Constant(null);


    private static ConstantExpression? _zero;
    public static ConstantExpression Zero => _zero ??= Expression.Constant(0);

}