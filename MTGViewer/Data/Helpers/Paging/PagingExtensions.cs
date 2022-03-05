using System;
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

        return OffsetExecute<TEntity>.ToOffsetList(query);
    }


    public static Task<OffsetList<TEntity>> ToOffsetListAsync<TEntity>(
        this IQueryable<TEntity> source,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return OffsetExecute<TEntity>.ToOffsetListAsync(source, cancel);
    }



    public static IQueryable<TEntity> After<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var (originSource, selector) = SelectQueries.GetSelectQuery<TOrigin, TEntity>(source);

        ArgumentNullException.ThrowIfNull(originSource, nameof(source));

        var seekCondition = OriginFilter.Create(originSource, origin, SeekDirection.Forward);

        var withSeek = originSource
            .Where(seekCondition);

        return selector is null
            ? withSeek.OfType<TEntity>()
            : withSeek.Select(selector);
    }


    public static IQueryable<TEntity> AfterBy<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin origin,
        Expression<Func<TEntity, TOrigin>> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var seekCondition = OriginFilter.Create(source, origin, SeekDirection.Forward, selector);

        return source.Where(seekCondition);
    }



    public static IQueryable<TEntity> Before<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var (originSource, selector) = SelectQueries.GetSelectQuery<TOrigin, TEntity>(source);

        ArgumentNullException.ThrowIfNull(originSource, nameof(origin));

        var seekCondition = OriginFilter.Create(originSource, origin, SeekDirection.Backwards);

        var withSeek = originSource
            .Reverse()
            .Where(seekCondition)
            .Reverse();

        return selector is null
            ? withSeek.OfType<TEntity>()
            : withSeek.Select(selector);
    }


    public static IQueryable<TEntity> BeforeBy<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin origin,
        Expression<Func<TEntity, TOrigin>> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var seekCondition = OriginFilter.Create(source, origin, SeekDirection.Backwards, selector);

        return source
            .Reverse()
            .Where(seekCondition)
            .Reverse();
    }


    public static IQueryable<TEntity> SeekOrigin<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin? origin, 
        int pageSize,
        bool backtrack)
        where TEntity : class
        where TOrigin : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var (originSource, selector) = SelectQueries.GetSelectQuery<TOrigin, TEntity>(source);

        ArgumentNullException.ThrowIfNull(originSource, nameof(origin));

        var direction = backtrack
            ? SeekDirection.Backwards
            : SeekDirection.Forward;

        var withSeek = originSource.SeekOrigin(origin, pageSize, direction);

        return selector is null
            ? withSeek.OfType<TEntity>()
            : withSeek.Select(selector);
    }


    private static IQueryable<TEntity> SeekOrigin<TEntity>(
        this IQueryable<TEntity> source,
        TEntity? origin,
        int pageSize,
        SeekDirection direction)
    {
        if (origin is null)
        {
            return direction is SeekDirection.Forward
                ? source
                    .Take(pageSize)
                : source
                    .Reverse()
                    .Take(pageSize)
                    .Reverse();
        }

        var seekCondition = OriginFilter.Create(source, origin, direction);

        return direction is SeekDirection.Forward
            ? source
                .Where(seekCondition)
                .Take(pageSize)
            : source
                .Reverse()
                .Where(seekCondition)
                .Take(pageSize)
                .Reverse();
    }


    public static SeekBuilder<TEntity, TEntity> SeekBy<TEntity>(
        this IQueryable<TEntity> source,
        int pageSize,
        bool backtrack)
        where TEntity : class
    {
        return new SeekBuilder<TEntity, TEntity>(source, pageSize, backtrack);
    }


    public static Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        this IQueryable<TEntity> source,
        CancellationToken cancel = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        return SeekExecute<TEntity>.ToSeekListAsync(source, cancel);
    }

}