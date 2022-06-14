using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal class SeekQuery<T> : ISeekQueryable<T>, IAsyncEnumerable<T>
    where T : class
{
    private readonly SeekProvider<T> _provider;

    public SeekQuery(SeekProvider<T> provider, Expression expression)
    {
        _provider = provider;
        Expression = expression;
    }

    public Expression Expression { get; }

    public Type ElementType => typeof(T);

    public IQueryProvider Provider => _provider;

    public IAsyncQueryProvider AsyncProvider => _provider;

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)_provider.Execute(Expression)!).GetEnumerator();

    public IEnumerator<T> GetEnumerator()
        => _provider.Execute<IEnumerable<T>>(Expression).GetEnumerator();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken)
        => _provider
            .ExecuteAsync<IAsyncEnumerable<T>>(Expression, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
}

internal class OrderedSeekQuery<T> : SeekQuery<T>, IOrderedQueryable<T>
    where T : class
{
    public OrderedSeekQuery(SeekProvider<T> provider, Expression expression)
        : base(provider, expression)
    {
    }
}
