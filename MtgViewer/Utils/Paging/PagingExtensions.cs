using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore.Query;

using EntityFrameworkCore.Paging.Query;

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
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return ExecuteOffset<TEntity>.ToOffsetListAsync(source, cancel);
    }

    #endregion

    #region Seek

    public static IQueryable<TEntity> SeekBy<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin? origin,
        SeekDirection direction,
        int? size)
        where TOrigin : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var seekSource = source.AsSeekQueryable();

        var seek = new SeekExpression(
            seekSource.Expression,
            Expression.Constant(origin),
            direction,
            size);

        return seekSource.Provider
            .CreateQuery<TEntity>(seek);
    }

    public static IQueryable<TEntity> SeekBy<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin? origin,
        SeekDirection direction,
        int? size)
        where TOrigin : struct
    {
        ArgumentNullException.ThrowIfNull(source);

        var seekSource = source.AsSeekQueryable();

        var seek = new SeekExpression(
            seekSource.Expression,
            Expression.Constant(origin),
            direction,
            size);

        return seekSource.Provider
            .CreateQuery<TEntity>(seek);
    }

    public static IQueryable<TEntity> After<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin? origin,
        int? size)
        where TOrigin : class
        => source.SeekBy(origin, SeekDirection.Forward, size);

    public static IQueryable<TEntity> After<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin? origin,
        int? size)
        where TOrigin : struct
        => source.SeekBy(origin, SeekDirection.Forward, size);

    public static IQueryable<TEntity> Before<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin? origin,
        int? size)
        where TOrigin : class
        => source.SeekBy(origin, SeekDirection.Backwards, size);

    public static IQueryable<TEntity> Before<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin? origin,
        int? size)
        where TOrigin : struct
        => source.SeekBy(origin, SeekDirection.Backwards, size);

    internal static readonly MethodInfo ToSeekListMethodInfo
        = typeof(PagingExtensions)
            .GetTypeInfo()
            .GetDeclaredMethod(nameof(ToSeekList))!;

    public static SeekList<TEntity> ToSeekList<TEntity>(this IQueryable<TEntity> source)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var seekSource = source.AsSeekQueryable();

        return seekSource.Provider
            .Execute<SeekList<TEntity>>(
                Expression.Call(
                    instance: null,
                    method: ToSeekListMethodInfo
                        .MakeGenericMethod(typeof(TEntity)),
                    arguments: seekSource.Expression));
    }

    public static Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        this IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var seekSource = source.AsSeekQueryable();

        return seekSource.AsyncProvider
            .ExecuteAsync<Task<SeekList<TEntity>>>(
                Expression.Call(
                    instance: null,
                    method: ToSeekListMethodInfo
                        .MakeGenericMethod(typeof(TEntity)),
                    arguments: seekSource.Expression),
                cancellationToken);
    }

    private static ISeekQueryable<TEntity> AsSeekQueryable<TEntity>(this IQueryable<TEntity> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is ISeekQueryable<TEntity> seekQuery)
        {
            return seekQuery;
        }

        if (source.Provider is not IAsyncQueryProvider asyncProvider)
        {
            throw new InvalidOperationException("Query does not support async operations");
        }

        var provider = new SeekProvider<TEntity>(asyncProvider);

        return SeekQueryable.Create(provider, source.Expression);
    }

    #endregion
}
