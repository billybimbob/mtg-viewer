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
    public static PageBuilder<TEntity> PageBy<TEntity>(
        this IQueryable<TEntity> source,
        int? index, 
        int pageSize)
    {
        return new PageBuilder<TEntity>(source, index, pageSize);
    }


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


//     public static async Task<OffsetList<TEntity>> ToOffsetListAsync<TEntity>(
//         this IPagedQueryable<TEntity> source,
//         CancellationToken cancel = default)
//     {
//         if (source == null)
//         {
//             throw new ArgumentNullException(nameof(source));
//         }

//         int page = source.PageValues.PageIndex;
//         int pageSize = source.PageValues.PageSize;
//         int totalItems = await source.CountAsync(cancel).ConfigureAwait(false);

//         var items = await source
//             .Skip(page * pageSize)
//             .Take(pageSize)
//             .ToListAsync(cancel)
//             .ConfigureAwait(false);

//         var offset = new Offset(page, totalItems, pageSize);

//         return new(offset, items);
//     }


//     public static IPagedQueryable<TEntity> PageBy<TEntity>(
//         this IQueryable<TEntity> source,
//         int? index, 
//         int pageSize)
//     {
//         if (source is null)
//         {
//             throw new ArgumentNullException(nameof(source));
//         }

//         if (pageSize < 0)
//         {
//             throw new ArgumentException(nameof(pageSize));
//         }

//         if (index < 0)
//         {
//             throw new ArgumentException(nameof(index));
//         }

//         if (source is IPagedQueryable<TEntity> page)
//         {
//             page.PageValues.PageIndex = index ?? 0;
//             page.PageValues.PageSize = pageSize;

//             return page;
//         }

//         return new PageQuery<TEntity>(
//             source,
//             new PageValues<TEntity>
//             {
//                 PageIndex = index ?? 0,
//                 PageSize = pageSize
//             });
//     }


//     public static IPagedQueryable<TEntity> AsForwardSeek<TEntity>(this IPagedQueryable<TEntity> source)
//     {
//         if (source is null)
//         {
//             throw new ArgumentNullException(nameof(source));
//         }

//         source.PageValues.Direction = SeekDirection.Forward;

//         return source;
//     }


//     public static IPagedQueryable<TEntity> AsBackwardsSeek<TEntity>(this IPagedQueryable<TEntity> source)
//     {
//         if (source is null)
//         {
//             throw new ArgumentNullException(nameof(source));
//         }

//         source.PageValues.Direction = SeekDirection.Backwards;

//         return source;
//     }


//     public static IPagedQueryable<TEntity> Where<TEntity>(
//         this IPagedQueryable<TEntity> source,
//         Expression<Func<TEntity, bool>> filter)
//     {
//         if (source is null)
//         {
//             throw new ArgumentNullException(nameof(source));
//         }

//         source.PageValues.Filter = filter;

//         return source;
//     }


//     public static IQueryable<TEntity> CreateQuery<TEntity>(
//         this IPagedQueryable<TEntity> source)
//     {
//         if (source is null)
//         {
//             throw new ArgumentNullException(nameof(source));
//         }

//         var pageValues = source.PageValues;

//         int index = pageValues.PageIndex;
//         int pageSize = pageValues.PageSize;

//         if (pageValues.Filter is not Expression<Func<TEntity, bool>> filter)
//         {
//             return source
//                 .Skip(index * pageSize)
//                 .Take(pageSize);
//         }

//         if (pageValues.Direction is SeekDirection.Backwards)
//         {
//             return source
//                 .Reverse()
//                 .Where(filter)
//                 .Take(pageSize);
//         }

//         return source
//             .Where(filter)
//             .Take(pageSize);
//     }


//     public static Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
//         this IPagedQueryable<TEntity> source,
//         CancellationToken cancel = default) where TEntity : class
//     {
//         if (source is null)
//         {
//             throw new ArgumentNullException(nameof(source));
//         }

//         var pageValues = source.PageValues;

//         int index = pageValues.PageIndex;
//         int pageSize = pageValues.PageSize;

//         if (pageValues.Filter is not Expression<Func<TEntity, bool>> filter)
//         {
//             return ToSeekListAsync(source, pageSize, cancel);
//         }

//         return pageValues.Direction switch
//         {
//             SeekDirection.Backwards =>
//                 ToSeekBackListAsync(source, filter, index, pageSize, cancel),

//             SeekDirection.Forward or _ =>
//                 ToSeekListAsync(source, filter, index, pageSize, cancel)
//         };
//     }


//     private static async Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
//         IQueryable<TEntity> source,
//         int pageSize,
//         CancellationToken cancel) where TEntity : class
//     {
//         if (pageSize < 0)
//         {
//             throw new ArgumentException(nameof(pageSize));
//         }

//         var items = await source
//             .Take(pageSize)
//             .ToListAsync(cancel)
//             .ConfigureAwait(false);

//         bool hasNext = await source
//             .Skip(pageSize) // offset is constant, so should be fine, keep eye on
//             .AnyAsync(cancel)
//             .ConfigureAwait(false);

//         var seek = new Seek<TEntity>(index: null, hasNext, items);

//         return new(seek, items);
//     }



//     private static async Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
//         IQueryable<TEntity> source,
//         Expression<Func<TEntity, bool>> seekCondition,
//         int index,
//         int pageSize,
//         CancellationToken cancel) where TEntity : class
//     {
//         if (pageSize < 0)
//         {
//             throw new ArgumentException(nameof(pageSize));
//         }

//         if (index < 0)
//         {
//             throw new ArgumentException(nameof(index));
//         }

//         var items = await source
//             .Where(seekCondition)
//             .Take(pageSize)
//             .ToListAsync(cancel)
//             .ConfigureAwait(false);

//         bool hasNext = await source
//             .Where(seekCondition)
//             .Skip(pageSize) // offset is constant, so should be fine, keep eye on
//             .AnyAsync(cancel)
//             .ConfigureAwait(false);

//         var seek = new Seek<TEntity>(index, hasNext, items);

//         return new(seek, items);
//     }


//     private static async Task<SeekList<TEntity>> ToSeekBackListAsync<TEntity>(
//         IQueryable<TEntity> source,
//         Expression<Func<TEntity, bool>> seekCondition,
//         int index,
//         int pageSize,
//         CancellationToken cancel) where TEntity : class
//     {
//         if (pageSize < 0)
//         {
//             throw new ArgumentException(nameof(pageSize));
//         }

//         if (index < 0)
//         {
//             throw new ArgumentException(nameof(index));
//         }

//         bool hasPrevious = await source
//             .Reverse()
//             .Where(seekCondition)
//             .Skip(pageSize) // offset is constant, so should be fine, keep eye on
//             .AnyAsync(cancel)
//             .ConfigureAwait(false);

//         if (hasPrevious && index == 0)
//         {
//             return await ResetSeekListAsync(source, pageSize, cancel)
//                 .ConfigureAwait(false);
//         }

//         var items = await source
//             .Reverse()
//             .Where(seekCondition)
//             .Take(pageSize)
//             .Reverse()
//             .ToListAsync(cancel)
//             .ConfigureAwait(false);

//         var seek = new Seek<TEntity>(hasPrevious, index, items);

//         return new(seek, items);
//     }


//     private static async Task<SeekList<TEntity>> ResetSeekListAsync<TEntity>(
//         IQueryable<TEntity> source,
//         int pageSize,
//         CancellationToken cancel) where TEntity : class
//     {
//         var items = await source
//             .Take(pageSize)
//             .ToListAsync(cancel)
//             .ConfigureAwait(false);

//         var seek = new Seek<TEntity>(index: null, hasNext: true, items);

//         return new(seek, items);
//     }
}