using System;
using System.Linq;
using System.Linq.Expressions;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;
using MTGViewer.Data.Internal;

namespace Microsoft.EntityFrameworkCore;

public static partial class PagingExtensions
{
    public static IQueryable<TEntity> PageBy<TEntity>(
        this IQueryable<TEntity> source,
        int? index,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

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
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        return new OffsetQuery<TEntity>(source)
            .ToOffsetListAsync(cancel);
    }


    public static Task<TEntity?> FindOriginAsync<TEntity, TKey>(
        this IQueryable<TEntity> source,
        TKey? seek,
        CancellationToken cancel = default)
        where TEntity : class
        where TKey : struct
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        if (seek is not TKey s)
        {
            return Task.FromResult<TEntity?>(null);
        }

        return FindNonNullOriginAsync(source, s, cancel);
    }


    public static Task<TEntity?> FindOriginAsync<TEntity, TKey>(
        this IQueryable<TEntity> source,
        TKey? seek,
        CancellationToken cancel = default)
        where TEntity : class
        where TKey : class?
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        if (seek is not TKey s)
        {
            return Task.FromResult<TEntity?>(null);
        }

        return FindNonNullOriginAsync(source, s, cancel);
    }


    private static Task<TEntity?> FindNonNullOriginAsync<TEntity, TKey>(
        IQueryable<TEntity> source,
        TKey seek,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entityId = EntityExtensions.GetKeyProperty<TEntity>();

        if (typeof(TKey) != entityId.PropertyType)
        {
            throw new ArgumentException($"{nameof(seek)} is the not correct key type");
        }

        var entityParameter = Expression.Parameter(
            typeof(TEntity), typeof(TEntity).Name[0].ToString().ToLower());

        var paramId = Expression.Property(entityParameter, entityId);

        var idLambda = Expression.Lambda<Func<TEntity, TKey>>(
            paramId,
            entityParameter);

        var equalSeek = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(paramId, Expression.Constant(seek)),
            entityParameter);

        return source
            .OrderBy(idLambda) // intentionally override order
            .AsNoTracking()
            .SingleOrDefaultAsync(equalSeek, cancellationToken);
    }


    public static IQueryable<TEntity> After<TEntity>(
        this IQueryable<TEntity> source,
        TEntity origin)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        ArgumentNullException.ThrowIfNull(origin, nameof(origin));

        var seekCondition = OriginFilter.Create(source, origin, SeekDirection.Forward);

        return source.Where(seekCondition);
    }


    public static IQueryable<TEntity> After<TEntity, TOrigin>(
        this IQueryable<TEntity> source,
        TOrigin origin,
        Expression<Func<TEntity, TOrigin>> selector)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        ArgumentNullException.ThrowIfNull(origin, nameof(origin));

        var seekCondition = OriginFilter.Create(source, origin, SeekDirection.Forward, selector);

        return source.Where(seekCondition);
    }


    public static IQueryable<TEntity> Before<TEntity>(
        this IQueryable<TEntity> source,
        TEntity origin)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        ArgumentNullException.ThrowIfNull(origin, nameof(origin));

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
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        ArgumentNullException.ThrowIfNull(origin, nameof(origin));

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
        where TEntity : class?
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

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


    public static Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        this IQueryable<TEntity> source,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        return new SeekQuery<TEntity>(source).ToSeekListAsync(cancel);
    }


    public static async Task<SeekList<TEntity>> ToSeekListAsync<TEntity, TKey>(
        this IQueryable<TEntity> source,
        TKey? seek,
        int pageSize,
        bool backtrack,
        CancellationToken cancel = default)
        where TEntity : class
        where TKey : struct
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        if (pageSize <= 0)
        {
            throw new ArgumentException(nameof(pageSize));
        }

        var origin = await source
            .FindOriginAsync(seek, cancel)
            .ConfigureAwait(false);

        return await source
            .SeekBy(origin, pageSize, backtrack)
            .ToSeekListAsync(cancel)
            .ConfigureAwait(false);
    }
    

    public static async Task<SeekList<TEntity>> ToSeekListAsync<TEntity, TKey>(
        this IQueryable<TEntity> source,
        TKey? seek,
        int pageSize,
        bool backtrack,
        CancellationToken cancel = default)
        where TEntity : class
        where TKey : class?
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        if (pageSize <= 0)
        {
            throw new ArgumentException(nameof(pageSize));
        }

        var origin = await source
            .FindOriginAsync(seek, cancel)
            .ConfigureAwait(false);

        return await source
            .SeekBy(origin, pageSize, backtrack)
            .ToSeekListAsync(cancel)
            .ConfigureAwait(false);
    }
}