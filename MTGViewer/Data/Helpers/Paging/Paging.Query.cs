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
    public static Task<OffsetList<TEntity>> ToOffsetListAsync<TEntity>(
        this IQueryable<TEntity> source,
        int? index, 
        int pageSize,
        CancellationToken cancel = default)
    {
        return source
            .PageBy(index, pageSize)
            .ToOffsetListAsync(cancel);
    }


    public static async Task<SeekList<TEntity>> ToSeekListAsync<TEntity, TKey>(
        this IQueryable<TEntity> source,
        int? index,
        int pageSize,
        Nullable<TKey> seek,
        bool backtrack,
        CancellationToken cancel = default)
        where TEntity : class
        where TKey : struct
    {
        var origin = seek is not TKey s
            ? null
            : await source
                .FindOriginAsync(s, cancel)
                .ConfigureAwait(false);

        return await source
            .OriginToSeekListAsync(origin, index, pageSize, backtrack, cancel)
            .ConfigureAwait(false);
    }


    public static async Task<SeekList<TEntity>> ToSeekListAsync<TEntity, TKey>(
        this IQueryable<TEntity> source,
        int? index,
        int pageSize,
        TKey? seek,
        bool backtrack,
        CancellationToken cancel = default)
        where TEntity : class
        where TKey : class?
    {
        var origin = seek is null
            ? null
            : await source
                .FindOriginAsync(seek, cancel)
                .ConfigureAwait(false);

        return await source
            .OriginToSeekListAsync(origin, index, pageSize, backtrack, cancel)
            .ConfigureAwait(false);
    }


    private static Task<TEntity?> FindOriginAsync<TEntity, TKey>(
        this IQueryable<TEntity> source,
        TKey seek,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entityId = EntityExtensions.GetKeyProperty<TEntity>();

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


    private static Task<SeekList<TEntity>> OriginToSeekListAsync<TEntity>(
        this IQueryable<TEntity> source,
        TEntity? origin,
        int? index,
        int pageSize,
        bool backtrack,
        CancellationToken cancel) where TEntity : class?
    {
        if (origin == default || index is not int i)
        {
            return source
                .PageBy(null, pageSize)
                .ToSeekListAsync(cancel);
        }

        return backtrack
            ? source
                .PageBy(i, pageSize)
                .Before(origin)
                .ToSeekListAsync(cancel)

            : source
                .PageBy(i, pageSize)
                .After(origin)
                .ToSeekListAsync(cancel);
    }
}