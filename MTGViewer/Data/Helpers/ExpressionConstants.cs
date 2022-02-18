using System.Collections.Generic;
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


    private static MethodInfo? _enumerableAny;
    public static MethodInfo EnumerableAny =>
        _enumerableAny ??=
            new Func<IEnumerable<object>, Func<object, bool>, bool>(Enumerable.All).Method;

    public static MethodInfo GetEnumerableAny<T>() => MakeGeneric<T>(EnumerableAny);


    private static MethodInfo? _queryableSkip;
    public static MethodInfo QueryableSkip =>
        _queryableSkip ??=
            new Func<IQueryable<object>, int, IQueryable<object>>(Queryable.Skip).Method;

    public static MethodInfo GetQueryableSkip<T>() => MakeGeneric<T>(QueryableSkip);


    private static MethodInfo? _queryableTake;
    public static MethodInfo QueryableTake =>
        _queryableTake ??=
            new Func<IQueryable<object>, int, IQueryable<object>>(Queryable.Take).Method;

    public static MethodInfo GetQueryableTake<T>() => MakeGeneric<T>(QueryableTake);


    private static MethodInfo? _queryableReverse;
    public static MethodInfo QueryableReverse =>
        _queryableReverse ??=
            new Func<IQueryable<object>, IQueryable<object>>(Queryable.Reverse).Method;

    public static MethodInfo GetQueryableReverse<T>() => MakeGeneric<T>(QueryableReverse);


    private static MethodInfo? _queryableWhere;
    public static MethodInfo QueryableWhere =>
        _queryableWhere ??=
            new Func<IQueryable<object>, Expression<Func<object, bool>>, IQueryable<object>>(Queryable.Where).Method;

    public static MethodInfo GetQueryableWhere<T>() => MakeGeneric<T>(QueryableWhere);


    private static ConstantExpression? _null;
    public static ConstantExpression Null => _null ??= Expression.Constant(null);


    private static ConstantExpression? _zero;
    public static ConstantExpression Zero => _zero ??= Expression.Constant(0);


    private static MethodInfo MakeGeneric<T>(MethodInfo baseMethod)
    {
        return baseMethod
            .GetGenericMethodDefinition()
            .MakeGenericMethod(typeof(T));
    }
}