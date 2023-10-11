using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal class SeekQuery<T> : ISeekable<T>, IOrderedQueryable<T>, IAsyncEnumerable<T>
{
    public SeekQuery(SeekProvider provider, Expression expression)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(expression);

        if (!expression.Type.IsAssignableTo(typeof(IQueryable<T>)))
        {
            throw new ArgumentException("Expression must be a query", nameof(expression));
        }

        AsyncProvider = provider;
        Expression = expression;
    }

    public Expression Expression { get; }

    public IAsyncQueryProvider AsyncProvider { get; }

    public IQueryProvider Provider => AsyncProvider;

    public Type ElementType => typeof(T);

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)Provider.Execute(Expression)!)
            .GetEnumerator();

    public IEnumerator<T> GetEnumerator()
        => Provider
            .Execute<IEnumerable<T>>(Expression)
            .GetEnumerator();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken)
        => AsyncProvider
            .ExecuteAsync<IAsyncEnumerable<T>>(Expression, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
}

internal static class SeekQuery
{
    public static IQueryable Create(SeekProvider provider, Expression expression)
    {
        var elementType = FindElementType(expression.Type)
            ?? throw new ArgumentException("Expression must be a query", nameof(expression));

        var seekQueryType = typeof(SeekQuery<>).MakeGenericType(elementType);

        return (IQueryable)Activator
            .CreateInstance(seekQueryType, provider, expression)!;
    }

    private static Type? FindElementType(Type queryType)
    {
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
