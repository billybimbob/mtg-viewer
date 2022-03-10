using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Paging.Query;
using System.Threading;
using System.Threading.Tasks;

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
            throw new ArgumentException(nameof(index));
        }

        if (pageSize <= 0)
        {
            throw new ArgumentException(nameof(pageSize));
        }

        int pageIndex = index ?? 0;

        return source
            .Skip(pageIndex * pageSize)
            .Take(pageSize);
    }
    

    public static OffsetList<TEntity> ToOffsetList<TEntity>(
        this IEnumerable<TEntity> source,
        int? pageIndex,
        int pageSize)
    {
        var query = source
            .AsQueryable()
            .PageBy(pageIndex, pageSize);

        return ExecuteOffset<TEntity>.ToOffsetList(query);
    }


    public static Task<OffsetList<TEntity>> ToOffsetListAsync<TEntity>(
        this IQueryable<TEntity> source,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return ExecuteOffset<TEntity>.ToOffsetListAsync(source, cancel);
    }



    private sealed class SelectQuery<TSource, TResult> : ISelectableQueryable<TSource, TResult>
    {
        private IQueryable<TResult> _query;
        public SelectQuery(IQueryable<TResult> query)
        {
            ArgumentNullException.ThrowIfNull(query);

            _query = query;
        }

        public IQueryProvider Provider => _query.Provider;
        public Expression Expression => _query.Expression;
        public Type ElementType => _query.ElementType;


        public IEnumerator<TResult> GetEnumerator() => _query.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }


    public static ISelectableQueryable<TSource, TResult> WithSelect<TSource, TResult>(
        this IQueryable<TResult> source)
    {
        return new SelectQuery<TSource, TResult>(source);
    }



    public static IQueryable<TEntity> After<TEntity>(
        this IQueryable<TEntity> source,
        TEntity origin)
        where TEntity : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var seekCondition = OriginFilter.Build(source, origin, SeekDirection.Forward);

        return source
            .Where(seekCondition);
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

        var query = originSource
            .Where(seekCondition)
            .Select(selector);

        return new SelectQuery<TSource, TResult>(query);
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

        var query = originSource
            .Where(seekCondition)
            .Select(selector);

        return new SelectQuery<TSource, TResult>(query);
    }



    public static IQueryable<TEntity> Before<TEntity>(
        this IQueryable<TEntity> source,
        TEntity origin)
        where TEntity : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var seekCondition = OriginFilter.Build(source, origin, SeekDirection.Backwards);

        return source
            .Reverse()
            .Where(seekCondition)
            .Reverse();
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

        var query = originSource
            .Reverse()
            .Where(seekCondition)
            .Reverse()
            .Select(selector);

        return new SelectQuery<TSource, TResult>(query);
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

        var query = originSource
            .Reverse()
            .Where(seekCondition)
            .Reverse()
            .Select(selector);

        return new SelectQuery<TSource, TResult>(query);
    }


    public static IQueryable<TEntity> SeekSize<TEntity>(
        this IQueryable<TEntity> source,
        int size)
    {
        ArgumentNullException.ThrowIfNull(source);

        var insertVisitor = new InsertTakeVisitor<TEntity>(size);

        return source.Provider
            .CreateQuery<TEntity>(insertVisitor.Visit(source.Expression));
    }


    public static IQueryable<TResult> SeekSize<TSource, TResult>(
        this ISelectableQueryable<TSource, TResult> source,
        int size)
    {
        ArgumentNullException.ThrowIfNull(source);

        var insertVisitor = new InsertTakeVisitor<TSource>(size);

        return source.Provider
            .CreateQuery<TResult>(insertVisitor.Visit(source.Expression));
    }



    public static IQueryable<TEntity> SeekOrigin<TEntity>(
        this IQueryable<TEntity> source,
        TEntity? origin,
        int pageSize,
        bool backtrack)
        where TEntity : notnull
    {
        var direction = backtrack
            ? SeekDirection.Backwards
            : SeekDirection.Forward;

        return (origin, direction) switch
        {
            (not null, SeekDirection.Forward) =>
                source
                    .After(origin)
                    .SeekSize(pageSize),

            (not null, SeekDirection.Backwards) =>
                source
                    .Before(origin)
                    .SeekSize(pageSize),

            (null, SeekDirection.Backwards) =>
                source
                    .Reverse()
                    .Take(pageSize)
                    .Reverse(),

            (null, SeekDirection.Forward) or _ =>
                source
                    .Take(pageSize),
        };
    }


    public static IQueryable<TResult> SeekOrigin<TEntity, TResult>(
        this ISelectableQueryable<TEntity, TResult> source,
        TEntity? origin, 
        int pageSize,
        bool backtrack)
        where TEntity : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var (originSource, selector) = SelectQueries.GetSelectQuery<TEntity, TResult>(source);

        return originSource
            .SeekOrigin(origin, pageSize, backtrack)
            .Select(selector);
    }


    public static IQueryable<TResult> SeekOrigin<TEntity, TResult>(
        this ISelectableQueryable<TEntity, TResult> source,
        TResult? origin, 
        int pageSize,
        bool backtrack)
        where TEntity : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var direction = backtrack
            ? SeekDirection.Backwards
            : SeekDirection.Forward;

        return (origin, direction) switch
        {
            (not null, SeekDirection.Forward) =>
                source
                    .After(origin)
                    .SeekSize(pageSize),

            (not null, SeekDirection.Backwards) =>
                source
                    .Before(origin)
                    .SeekSize(pageSize),

            (null, SeekDirection.Backwards) =>
                source
                    .Reverse()
                    .Take(pageSize)
                    .Reverse(),

            (null, SeekDirection.Forward) or _ =>
                source
                    .Take(pageSize),
        };
    }


    public static ISeekBuilder<TEntity> SeekBy<TEntity>(
        this IQueryable<TEntity> source,
        int pageSize,
        bool backtrack)
        where TEntity : class
    {
        return new ResultNullSeek<TEntity>(source, pageSize, backtrack);
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