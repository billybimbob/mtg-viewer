using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore.Query;

using EntityFrameworkCore.Paging.Query;
using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging;

public static class PagingExtensions
{
    #region Offset

    public static IQueryable<TEntity> PageBy<TEntity>(
        this IQueryable<TEntity> source,
        int? index,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (index < 0)
        {
            throw new ArgumentException("Index is negative", nameof(index));
        }

        if (pageSize <= 0)
        {
            throw new ArgumentException("Page size is not a positive nonzero value", nameof(pageSize));
        }

        int pageIndex = index ?? 0;

        return source
            .Skip(pageIndex * pageSize)
            .Take(pageSize);
    }

    public static OffsetList<TEntity> ToOffsetList<TEntity>(
        this IEnumerable<TEntity> source,
        int? index,
        int pageSize)
    {
        var query = source
            .AsQueryable()
            .PageBy(index, pageSize);

        return ExecuteOffset<TEntity>.ToOffsetList(query);
    }

    public static Task<OffsetList<TEntity>> ToOffsetListAsync<TEntity>(
        this IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return ExecuteOffset<TEntity>.ToOffsetListAsync(source, cancellationToken);
    }

    #endregion

    #region Seek

    public static ISeekable<TEntity> SeekBy<TEntity>(
        this IOrderedQueryable<TEntity> source,
        SeekDirection direction)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var seekable = source.AsSeekable();

        return seekable.Provider
            .CreateQuery<TEntity>(
                Expression.Call(
                    instance: null,
                    method: PagingMethods.SeekBy
                        .MakeGenericMethod(typeof(TEntity)),
                    arg0: seekable.Expression,
                    arg1: Expression.Constant(direction)))
            .AsSeekable();
    }

    public static ISeekable<TEntity> After<TEntity>(
        this ISeekable<TEntity> source,
        TEntity? origin)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Provider
            .CreateQuery<TEntity>(
                Expression.Call(
                    instance: null,
                    method: PagingMethods.AfterReference
                        .MakeGenericMethod(typeof(TEntity)),
                    arg0: source.Expression,
                    arg1: Expression.Constant(origin, typeof(TEntity))))
            .AsSeekable();
    }

    public static ISeekable<TEntity> After<TEntity>(
        this ISeekable<TEntity> source,
        Expression<Func<TEntity, bool>> originPredicate)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(originPredicate);

        var seekable = source.AsSeekable();

        return seekable.Provider
            .CreateQuery<TEntity>(
                Expression.Call(
                    instance: null,
                    method: PagingMethods.AfterPredicate
                        .MakeGenericMethod(typeof(TEntity)),
                    arg0: seekable.Expression,
                    arg1: Expression.Quote(originPredicate)))
            .AsSeekable();
    }

    public static SeekList<TEntity> ToSeekList<TEntity>(this IQueryable<TEntity> source)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var seekable = source.AsSeekable();

        return seekable.Provider
            .Execute<SeekList<TEntity>>(
                Expression.Call(
                    instance: null,
                    method: PagingMethods.ToSeekList
                        .MakeGenericMethod(typeof(TEntity)),
                    arguments: seekable.Expression));
    }

    public static Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        this IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var seekable = source.AsSeekable();

        return seekable.AsyncProvider
            .ExecuteAsync<Task<SeekList<TEntity>>>(
                Expression.Call(
                    instance: null,
                    method: PagingMethods.ToSeekList
                        .MakeGenericMethod(typeof(TEntity)),
                    arguments: seekable.Expression),
                cancellationToken);
    }

    private static ISeekable<TEntity> AsSeekable<TEntity>(this IQueryable<TEntity> source)
    {
        if (source is ISeekable<TEntity> seekable)
        {
            return seekable;
        }

        if (source.Provider is not IAsyncQueryProvider asyncProvider)
        {
            throw new InvalidOperationException("Query does not support async operations");
        }

        var provider = new SeekProvider(asyncProvider);

        return new SeekQuery<TEntity>(provider, source.Expression);
    }

    #endregion
}
