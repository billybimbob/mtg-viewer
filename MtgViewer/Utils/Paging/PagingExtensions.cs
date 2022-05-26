using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Paging.Query;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

namespace System.Paging;

public static class PagingExtensions
{
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

    private sealed class SelectableQueryable<TSource, TResult>
        : ISelectableQueryable<TSource, TResult>, IAsyncEnumerable<TResult>
    {
        private readonly IQueryable<TResult> _query;

        public SelectableQueryable(IQueryable<TResult> query)
        {
            ArgumentNullException.ThrowIfNull(query);

            _query = query;
        }

        public IQueryProvider Provider => _query.Provider;
        public Expression Expression => _query.Expression;
        public Type ElementType => _query.ElementType;

        public IAsyncEnumerator<TResult> GetAsyncEnumerator(CancellationToken cancel = default) =>
            _query.AsAsyncEnumerable().GetAsyncEnumerator(cancel);

        public IEnumerator<TResult> GetEnumerator() => _query.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static ISelectableQueryable<TSource, TResult> WithSelect<TSource, TResult>(this IQueryable<TResult> source)
        => new SelectableQueryable<TSource, TResult>(source);

    public static ISelectableQueryable<TEntity, TEntity> After<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin origin)
        where TEntity : notnull
        where TOrigin : TEntity
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var seekCondition = OriginFilter.Build(source, origin, SeekDirection.Forward);

        return source
            .Where(seekCondition)
            .WithSelect<TEntity, TEntity>();
    }

    public static ISelectableQueryable<TSource, TResult> After<TSource, TResult>(
        this ISelectableQueryable<TSource, TResult> source,
        TSource origin)
        where TSource : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var (originSource, selector) = SelectQueries.GetSelectQuery<TSource, TResult>(source);

        var seekCondition = OriginFilter.Build(originSource, origin, SeekDirection.Forward);

        return originSource
            .Where(seekCondition)
            .Select(selector)
            .WithSelect<TSource, TResult>();
    }

    public static ISelectableQueryable<TSource, TResult> After<TSource, TResult>(
        this ISelectableQueryable<TSource, TResult> source,
        TResult origin)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var (originSource, selector) = SelectQueries.GetSelectQuery<TSource, TResult>(source);

        var seekCondition = OriginFilter.Build(originSource, origin, SeekDirection.Forward, selector);

        return originSource
            .Where(seekCondition)
            .Select(selector)
            .WithSelect<TSource, TResult>();
    }

    public static ISelectableQueryable<TEntity, TEntity> Before<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin origin)
        where TEntity : notnull
        where TOrigin : TEntity
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var seekCondition = OriginFilter.Build(source, origin, SeekDirection.Backwards);

        return source
            .Reverse()
            .Where(seekCondition)
            .Reverse()
            .WithSelect<TEntity, TEntity>();
    }

    public static ISelectableQueryable<TSource, TResult> Before<TSource, TResult>(
        this ISelectableQueryable<TSource, TResult> source,
        TSource origin)
        where TSource : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var (originSource, selector) = SelectQueries.GetSelectQuery<TSource, TResult>(source);

        var seekCondition = OriginFilter.Build(originSource, origin, SeekDirection.Backwards);

        return originSource
            .Reverse()
            .Where(seekCondition)
            .Reverse()
            .Select(selector)
            .WithSelect<TSource, TResult>();
    }

    public static ISelectableQueryable<TSource, TResult> Before<TSource, TResult>(
        this ISelectableQueryable<TSource, TResult> source,
        TResult origin)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var (originSource, selector) = SelectQueries.GetSelectQuery<TSource, TResult>(source);

        var seekCondition = OriginFilter
            .Build(originSource, origin, SeekDirection.Backwards, selector);

        return originSource
            .Reverse()
            .Where(seekCondition)
            .Reverse()
            .Select(selector)
            .WithSelect<TSource, TResult>();
    }

    public static IQueryable<TResult> Take<TSource, TResult>(
        this ISelectableQueryable<TSource, TResult> source,
        int count)
    {
        ArgumentNullException.ThrowIfNull(source);

        var insertVisitor = new InsertTakeVisitor<TSource>(count);

        return source.Provider
            .CreateQuery<TResult>(insertVisitor.Visit(source.Expression));
    }

    public static ISelectableQueryable<TEntity, TEntity> SeekOrigin<TEntity>(
        this IQueryable<TEntity> source,
        TEntity? origin,
        SeekDirection direction)
        where TEntity : notnull
    {
        return (origin, direction) switch
        {
            (not null, SeekDirection.Forward) => source.After(origin),
            (not null, SeekDirection.Backwards) => source.Before(origin),

            (null, SeekDirection.Backwards) => source.SeekBackwards(),

            (null, _) or _ => source.WithSelect<TEntity, TEntity>(),
        };
    }

    private static ISelectableQueryable<TEntity, TEntity> SeekBackwards<TEntity>(
        this IQueryable<TEntity> source)
    {
        return source
            .Reverse()
            .Reverse()
            .WithSelect<TEntity, TEntity>();
    }

    public static ISelectableQueryable<TSource, TResult> SeekOrigin<TSource, TResult>(
        this ISelectableQueryable<TSource, TResult> source,
        TSource? origin,
        SeekDirection direction)
        where TSource : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var (originSource, selector) = SelectQueries.GetSelectQuery<TSource, TResult>(source);

        return originSource
            .SeekOrigin(origin, direction)
            .Select(selector)
            .WithSelect<TSource, TResult>();
    }

    public static ISelectableQueryable<TSource, TResult> SeekOrigin<TSource, TResult>(
        this ISelectableQueryable<TSource, TResult> source,
        TResult? origin,
        SeekDirection direction)
        where TSource : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(source);

        return (origin, direction) switch
        {
            (not null, SeekDirection.Forward) => source.After(origin),
            (not null, SeekDirection.Backwards) => source.Before(origin),

            (null, SeekDirection.Backwards) => source.SeekBackwards(),
            (null, _) or _ => source,
        };
    }

    private static ISelectableQueryable<TSource, TResult> SeekBackwards<TSource, TResult>(
        this ISelectableQueryable<TSource, TResult> source)
    {
        var (originSource, selector) = SelectQueries.GetSelectQuery<TSource, TResult>(source);

        return originSource
            .Reverse()
            .Reverse()
            .Select(selector)
            .WithSelect<TSource, TResult>();
    }

    public static ISeekable<TEntity> SeekBy<TEntity, TKey>(
        this IQueryable<TEntity> source,
        TKey? key,
        SeekDirection direction)
        where TEntity : class
        where TKey : class
    {
        // use ValueTuple as a placeholder type

        return new EntityOriginSeek<TEntity, TKey, ValueTuple>(
            source, direction, null, key, null);
    }

    public static ISeekable<TEntity> SeekBy<TEntity, TKey>(
        this IQueryable<TEntity> source,
        TKey? key,
        SeekDirection direction)
        where TEntity : class
        where TKey : struct
    {
        // use object as a placeholder type

        return new EntityOriginSeek<TEntity, object, TKey>(
            source, direction, null, null, key);
    }

    public static Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        this IQueryable<TEntity> source,
        CancellationToken cancel = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        return ExecuteSeek<TEntity>.ToSeekListAsync(source, cancel);
    }
}
