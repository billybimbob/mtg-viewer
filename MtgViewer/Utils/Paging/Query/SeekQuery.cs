using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;

using EntityFrameworkCore.Paging.Query.Infrastructure;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal class SeekQuery<T> : ISeekable<T>, IOrderedQueryable<T>, IAsyncEnumerable<T>
{
    private readonly SeekProvider _seekProvider;

    public SeekQuery(SeekProvider provider, Expression expression)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(expression);

        if (!expression.Type.IsAssignableTo(typeof(IQueryable<T>)))
        {
            throw new ArgumentException("Expression must be a query", nameof(expression));
        }

        _seekProvider = provider;
        Expression = expression;
    }

    public Expression Expression { get; }

    public Type ElementType => typeof(T);

    public IQueryProvider Provider => _seekProvider;

    public IAsyncQueryProvider AsyncProvider => _seekProvider;

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)_seekProvider.Execute(Expression)!)
            .GetEnumerator();

    public IEnumerator<T> GetEnumerator()
        => _seekProvider
            .Execute<IEnumerable<T>>(Expression)
            .GetEnumerator();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken)
        => _seekProvider
            .ExecuteAsync<IAsyncEnumerable<T>>(Expression, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
}

internal static class SeekQuery
{
    public static IQueryable Create(SeekProvider provider, Expression expression)
    {
        var elementType = QueryTypeHelpers.FindElementType(expression)
            ?? throw new ArgumentException("Expression must be a query", nameof(expression));

        var seekQueryType = typeof(SeekQuery<>).MakeGenericType(elementType);

        return (IQueryable)Activator
            .CreateInstance(seekQueryType, provider, expression)!;
    }
}
