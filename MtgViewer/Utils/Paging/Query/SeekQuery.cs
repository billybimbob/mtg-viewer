using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal static class SeekQuery
{
    public static IQueryable Create(SeekProvider provider, Expression expression)
    {
        if (!expression.Type.IsAssignableTo(typeof(IQueryable)))
        {
            throw new ArgumentException("Expression must be a query", nameof(expression));
        }

        var elementType = expression.Type.GenericTypeArguments.ElementAtOrDefault(0);

        if (elementType is null)
        {
            throw new ArgumentException("Expression must be strongly typed", nameof(expression));
        }

        var seekQueryType = IsOrderedQuery(expression)
            ? typeof(OrderedSeekQuery<>).MakeGenericType(elementType)
            : typeof(SeekQuery<>).MakeGenericType(elementType);

        return (IQueryable)Activator
            .CreateInstance(seekQueryType, provider, expression)!;
    }

    public static ISeekQueryable<T> Create<T>(SeekProvider provider, Expression expression)
    {
        if (!expression.Type.IsAssignableTo(typeof(IQueryable<T>)))
        {
            throw new ArgumentException(
                $"{expression.Type.Name} is not {typeof(IQueryable<T>).Name}", nameof(expression));
        }

        return IsOrderedQuery(expression)
            ? new OrderedSeekQuery<T>(provider, expression)
            : new SeekQuery<T>(provider, expression);
    }

    private static bool IsOrderedQuery(Expression query)
        => query.Type.IsAssignableTo(typeof(IOrderedQueryable));
}

internal class SeekQuery<TSource> : ISeekQueryable<TSource>, IAsyncEnumerable<TSource>
{
    public SeekQuery(SeekProvider provider, Expression expression)
    {
        AsyncProvider = provider;
        Expression = expression;
    }

    public Expression Expression { get; }

    public IAsyncQueryProvider AsyncProvider { get; }

    public IQueryProvider Provider => AsyncProvider;

    public Type ElementType => typeof(TSource);

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)AsyncProvider.Execute(Expression)!)
            .GetEnumerator();

    public IEnumerator<TSource> GetEnumerator()
        => AsyncProvider
            .Execute<IEnumerable<TSource>>(Expression)
            .GetEnumerator();

    public IAsyncEnumerator<TSource> GetAsyncEnumerator(CancellationToken cancellationToken)
        => AsyncProvider
            .ExecuteAsync<IAsyncEnumerable<TSource>>(Expression, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
}

internal class OrderedSeekQuery<TSource> : SeekQuery<TSource>, IOrderedQueryable<TSource>
{
    public OrderedSeekQuery(SeekProvider provider, Expression expression)
        : base(provider, expression)
    {
    }
}
