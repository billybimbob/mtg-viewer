using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal static class QueryableExtensions
{
    public static IQueryable Where(this IQueryable source, LambdaExpression predicate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        return source.Provider
            .CreateQuery(
                Expression.Call(
                    instance: null,
                    method: QueryableMethods.Where
                        .MakeGenericMethod(source.ElementType),
                    arg0: source.Expression,
                    arg1: Expression.Quote(predicate)));
    }

    public static IQueryable Take(this IQueryable source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Provider
            .CreateQuery(
                Expression.Call(
                    instance: null,
                    method: QueryableMethods.Take
                        .MakeGenericMethod(source.ElementType),
                    arg0: source.Expression,
                    arg1: Expression.Constant(count)));
    }

    public static IQueryable Reverse(this IQueryable source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Provider
            .CreateQuery(
                Expression.Call(
                    instance: null,
                    method: QueryableMethods.Reverse
                        .MakeGenericMethod(source.ElementType),
                    arguments: source.Expression));

    }

    public static object? FirstOrDefault(this IQueryable source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Provider
            .Execute(
                Expression.Call(
                    instance: null,
                    method: QueryableMethods.FirstOrDefaultWithoutPredicate
                        .MakeGenericMethod(source.ElementType),
                    arguments: source.Expression));
    }

    #region Dynamic Invokes

    public static async Task<object?> FirstOrDefaultAsync(
        this IQueryable source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Provider is not IAsyncQueryProvider asyncProvider)
        {
            throw new InvalidOperationException("Provider does not support async operations");
        }

        var resultTask = typeof(Task<>)
            .MakeGenericType(source.ElementType);

        var call = Expression.Call(
            instance: null,
            method: QueryableMethods.FirstOrDefaultWithoutPredicate
                .MakeGenericMethod(source.ElementType),
            arguments: source.Expression);

        var execute = (Task?)typeof(IAsyncQueryProvider)
            .GetTypeInfo()
            .GetMethod(nameof(IAsyncQueryProvider.ExecuteAsync))?
            .MakeGenericMethod(resultTask)
            .Invoke(asyncProvider, new object[] { call, cancellationToken });

        if (execute is null)
        {
            return null;
        }

        await execute;

        return resultTask
            .GetTypeInfo()
            .GetProperty(nameof(Task<object?>.Result))?
            .GetValue(execute);
    }

    // reference:
    // https://github.com/dotnet/efcore/blob/a4f5b82d7e15311ca0275e5171a68f529463dcc8/src/EFCore/Extensions/EntityFrameworkQueryableExtensions.cs#L2429
    private static readonly MethodInfo StringIncludeMethodInfo
        = typeof(EntityFrameworkQueryableExtensions)
            .GetTypeInfo()
            .GetDeclaredMethods(nameof(EntityFrameworkQueryableExtensions.Include))
            .Single(mi => mi
                .GetParameters()
                .Any(pi => pi.Name == "navigationPropertyPath" && pi.ParameterType == typeof(string)));

    public static IQueryable Include(this IQueryable source, string navigationPropertyPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(navigationPropertyPath);

        if (source.ElementType.IsValueType)
        {
            throw new InvalidOperationException($"{source.ElementType} is not a reference type");
        }

        return (IQueryable)StringIncludeMethodInfo
            .MakeGenericMethod(source.ElementType)
            .Invoke(null, new object?[] { source, navigationPropertyPath })!;
    }

    // reference:
    // https://github.com/dotnet/efcore/blob/a4f5b82d7e15311ca0275e5171a68f529463dcc8/src/EFCore/Extensions/EntityFrameworkQueryableExtensions.cs#L2530
    private static readonly MethodInfo AsNoTrackingMethodInfo
        = typeof(EntityFrameworkQueryableExtensions)
            .GetTypeInfo()
            .GetDeclaredMethod(nameof(EntityFrameworkQueryableExtensions.AsNoTracking))!;

    public static IQueryable AsNoTracking(this IQueryable source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.ElementType.IsValueType)
        {
            throw new InvalidOperationException($"{source.ElementType} is not a reference type");
        }

        return (IQueryable)AsNoTrackingMethodInfo
            .MakeGenericMethod(source.ElementType)
            .Invoke(null, new object?[] { source })!;
    }

    #endregion
}
