using System;
using System.Linq;
using System.Linq.Expressions;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore;

public static partial class PagingExtensions
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


    public static Task<OffsetList<TEntity>> ToOffsetListAsync<TEntity>(
        this IQueryable<TEntity> source,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new OffsetQuery<TEntity>(source)
            .ToOffsetListAsync(cancel);
    }




    public static IQueryable<TEntity> After<TEntity>(
        this IQueryable<TEntity> source,
        TEntity origin)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var seekCondition = OriginFilter.Create(source, origin, SeekDirection.Forward);

        return source.Where(seekCondition);
    }


    public static IQueryable<TEntity> After<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin origin,
        Expression<Func<TEntity, TOrigin>> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var seekCondition = OriginFilter.Create(source, origin, SeekDirection.Forward, selector);

        return source.Where(seekCondition);
    }


    public static IQueryable<TEntity> Before<TEntity>(
        this IQueryable<TEntity> source,
        TEntity origin)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(origin);

        var seekCondition = OriginFilter.Create(source, origin, SeekDirection.Backwards);

        return source
            .Reverse()
            .Where(seekCondition)
            .Reverse();
    }


    public static IQueryable<TEntity> Before<TEntity, TOrigin>(
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


    public static IQueryable<TEntity> SeekBy<TEntity>(
        this IQueryable<TEntity> source,
        TEntity? origin, 
        int pageSize,
        bool backtrack)
    {
        ArgumentNullException.ThrowIfNull(source);

        var direction = backtrack
            ? SeekDirection.Backwards
            : SeekDirection.Forward;

        if (origin is null)
        {
            return direction is SeekDirection.Forward
                ? source.Take(pageSize)
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


    public static SeekBuilder<TEntity, TKey> SeekBy<TEntity, TKey>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, TKey>> sourceKey,
        TKey? key,
        int pageSize,
        bool backtrack)
        where TEntity : class
        where TKey : IEquatable<TKey>
    {
        return new SeekBuilder<TEntity, TKey>(source, sourceKey, key, pageSize, backtrack);
    }


    public static SeekBuilder<TEntity, TKey> SeekBy<TEntity, TKey>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, TKey>> sourceKey,
        TKey? key,
        int pageSize,
        bool backtrack)
        where TEntity : class
        where TKey : struct, IEquatable<TKey>
    {
        return new NullableSeekBuilder<TEntity, TKey>(source, sourceKey, key, pageSize, backtrack);
    }
    

    public static Task<SeekList<TEntity, TKey>> ToSeekListAsync<TEntity, TKey>(
        this IQueryable<TEntity> source,
        Func<TEntity, TKey> key,
        CancellationToken cancel = default)
        where TKey : IEquatable<TKey>
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SeekQuery<TEntity, TKey>(source, key).ToSeekListAsync(cancel);
    }

}