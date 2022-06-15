using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal class SeekQuery<TSource> : ISeekQueryable<TSource>, IAsyncEnumerable<TSource>
{
    private readonly SeekProvider<TSource> _provider;

    public SeekQuery(SeekProvider<TSource> provider, Expression expression)
    {
        _provider = provider;
        Expression = expression;
    }

    public Expression Expression { get; }

    public Type ElementType => typeof(TSource);

    public IQueryProvider Provider => _provider;

    public IAsyncQueryProvider AsyncProvider => _provider;

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)_provider.Execute(Expression)!).GetEnumerator();

    public IEnumerator<TSource> GetEnumerator()
        => _provider.Execute<IEnumerable<TSource>>(Expression).GetEnumerator();

    public IAsyncEnumerator<TSource> GetAsyncEnumerator(CancellationToken cancellationToken)
        => _provider
            .ExecuteAsync<IAsyncEnumerable<TSource>>(Expression, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
}

internal class OrderedSeekQuery<TSource> : SeekQuery<TSource>, IOrderedQueryable<TSource>
{
    public OrderedSeekQuery(SeekProvider<TSource> provider, Expression expression)
        : base(provider, expression)
    {
    }
}
