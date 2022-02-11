using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Paging;
using System.Threading;
using Microsoft.EntityFrameworkCore.Query;

namespace Microsoft.EntityFrameworkCore.Paging;


// public interface IPagedQueryable<T> : IQueryable<T>
// {
//     public IPageValues<T> PageValues { get; }
// }


// public interface IPageValues<TEntity>
// {
//     public int PageSize { get; set; }

//     public int PageIndex { get; set; }

//     public SeekDirection Direction { get; set; }

//     public Expression<Func<TEntity, bool>>? Filter { get; set; }
// }



// internal class PageValues<TEntity> : IPageValues<TEntity>
// {
//     public int PageSize { get; set; } = 1;

//     public int PageIndex { get; set; } = 0;

//     public SeekDirection Direction { get; set; } = SeekDirection.Forward;

//     public Expression<Func<TEntity, bool>>? Filter { get; set; }

//     public PageValues<TResult> Copy<TResult>()
//     {
//         return new PageValues<TResult>
//         {
//             PageSize = PageSize,
//             PageIndex = PageIndex,
//             Direction = Direction,
//             Filter = null
//         };
//     }
// }



// internal sealed class PageQuery<TEntity> : IPagedQueryable<TEntity>, IAsyncEnumerable<TEntity>
// {
//     private readonly IQueryable<TEntity> _query;
//     private readonly PageQueryProvider<TEntity> _pageQueryProvider;

//     public PageQuery(IQueryable<TEntity> query, PageValues<TEntity> values)
//     {
//         _query = query;
//         _pageQueryProvider = GetQueryProvider(query.Provider, values);

//         PageValues = values;
//     }

//     public static PageQueryProvider<TEntity> GetQueryProvider(
//         IQueryProvider provider,
//         PageValues<TEntity> values)
//     {
//         return provider is IAsyncQueryProvider asyncProvider
//             ? new AsyncPageQueryProvider<TEntity>(asyncProvider, values)
//             : new PageQueryProvider<TEntity>(provider, values);
//     }

//     public IPageValues<TEntity> PageValues { get; }

//     public Type ElementType => _query.ElementType;

//     public Expression Expression => _query.Expression;

//     public IQueryProvider Provider => _query.Provider;


//     IEnumerator IEnumerable.GetEnumerator() => _query.GetEnumerator();

//     public IEnumerator<TEntity> GetEnumerator() => _query.GetEnumerator();

//     public IAsyncEnumerator<TEntity> GetAsyncEnumerator(CancellationToken cancellationToken) =>
//         _query.AsAsyncEnumerable().GetAsyncEnumerator(cancellationToken);
// }



// internal class PageQueryProvider<TEntity> : IQueryProvider
// {
//     private readonly IQueryProvider _provider;
//     private readonly PageValues<TEntity> _values;

//     public PageQueryProvider(IQueryProvider provider, PageValues<TEntity> values)
//     {
//         _provider = provider;
//         _values = values;
//     }

//     public IQueryable CreateQuery(Expression expression) =>
//         _provider.CreateQuery(expression);

//     public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
//     {
//         var query = _provider.CreateQuery<TElement>(expression);

//         return new PageQuery<TElement>(query, _values.Copy<TElement>());
//     }

//     public object? Execute(Expression expression) =>
//         _provider.Execute(expression);

//     public TResult Execute<TResult>(Expression expression) =>
//         _provider.Execute<TResult>(expression);
// }


// internal class AsyncPageQueryProvider<TEntity> : PageQueryProvider<TEntity>, IAsyncQueryProvider
// {
//     private readonly IAsyncQueryProvider _provider;

//     public AsyncPageQueryProvider(IAsyncQueryProvider provider, PageValues<TEntity> values)
//         : base(provider, values)
//     {
//         _provider = provider;
//     }

//     public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
//     {
//         return _provider.ExecuteAsync<TResult>(expression, cancellationToken);
//     }
// }