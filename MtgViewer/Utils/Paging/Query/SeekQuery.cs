using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class SeekQuery : IQueryable
{
    public SeekQuery(IQueryProvider provider, Expression expression, Type elementType)
    {
        Provider = provider;
        Expression = expression;
        ElementType = elementType;
    }

    public IQueryProvider Provider { get; }

    public Expression Expression { get; }

    public Type ElementType { get; }

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)Provider.Execute(Expression)!).GetEnumerator();
}

internal sealed class SeekQuery<T> : ISeekQueryable<T>, IAsyncEnumerable<T>
{
    private readonly IAsyncQueryProvider _provider;

    public SeekQuery(IAsyncQueryProvider provider, Expression expression)
    {
        _provider = provider;
        Expression = expression;
    }

    public Expression Expression { get; }

    public Type ElementType => typeof(T);

    public IQueryProvider Provider => _provider;

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)_provider.Execute(Expression)!).GetEnumerator();

    public IEnumerator<T> GetEnumerator()
        => _provider.Execute<IEnumerable<T>>(Expression).GetEnumerator();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken)
        => _provider
            .ExecuteAsync<IAsyncEnumerable<T>>(Expression, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
}
