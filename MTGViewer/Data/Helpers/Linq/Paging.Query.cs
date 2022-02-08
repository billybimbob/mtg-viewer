using System;
using System.Collections.Generic;
using System.Collections.Paging;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore;


public enum SeekPosition
{
    Start,
    End
}


public static partial class PagingExtensions
{
    public static async Task<OffsetList<TEntity>> ToOffsetListAsync<TEntity>(
        this IQueryable<TEntity> source,
        int? pageIndex,
        int pageSize, 
        CancellationToken cancel = default)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (pageSize < 0)
        {
            throw new ArgumentException(nameof(pageSize));
        }

        int page = pageIndex ?? 0;
        int totalItems = await source.CountAsync(cancel).ConfigureAwait(false);

        var items = await source
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        var offset = new Offset(page, totalItems, pageSize);

        return new(offset, items);
    }



    public static Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        this IQueryable<TEntity> source,
        SeekPosition position,
        int pageSize,
        CancellationToken cancel = default) where TEntity : class
    {
        return position switch
        {
            SeekPosition.End => ToSeekBackListAsync(source.Reverse(), pageSize, hasNext: false, cancel),
            SeekPosition.Start or _ => ToSeekListAsync(source, pageSize, hasPrevious: false, cancel)
        };
    }


    private static async Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        IQueryable<TEntity> source,
        int pageSize,
        bool hasPrevious,
        CancellationToken cancel) where TEntity : class
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (pageSize < 0)
        {
            throw new ArgumentException(nameof(pageSize));
        }

        var items = await source
            .Take(pageSize)
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        bool hasNext = await source
            .Skip(pageSize) // offset is constant, so should be fine, keep eye on
            .AnyAsync(cancel)
            .ConfigureAwait(false);

        var seek = new Seek<TEntity>(hasPrevious, hasNext, items);

        return new(seek, items);
    }


    private static async Task<SeekList<TEntity>> ToSeekBackListAsync<TEntity>(
        IQueryable<TEntity> source,
        int pageSize,
        bool hasNext,
        CancellationToken cancel) where TEntity : class
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (pageSize < 0)
        {
            throw new ArgumentException(nameof(pageSize));
        }

        var items = await source
            .Take(pageSize)
            .Reverse()
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        bool hasPrevious = await source
            .Skip(pageSize) // offset is constant, so should be fine, keep eye on
            .AnyAsync(cancel)
            .ConfigureAwait(false);

        var seek = new Seek<TEntity>(hasPrevious, hasNext, items);

        return new(seek, items);
    }


    public static Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, bool>> seekCondition,
        SeekPosition position,
        int pageSize,
        CancellationToken cancel = default) where TEntity : class
    {
        return position switch
        {
            SeekPosition.End => ToSeekBackListAsync(source.Reverse(), seekCondition, pageSize, cancel),
            SeekPosition.Start or _ => ToSeekListAsync(source, seekCondition, pageSize, cancel)
        };
    }


    private static Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        IQueryable<TEntity> source,
        Expression<Func<TEntity, bool>> seekCondition,
        int pageSize,
        CancellationToken cancel) where TEntity : class
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (seekCondition is null)
        {
            throw new ArgumentNullException(nameof(seekCondition));
        }

        source = source
            .Where(seekCondition);

        return ToSeekListAsync(source, pageSize, hasPrevious: true, cancel);
    }
    

    private static Task<SeekList<TEntity>> ToSeekBackListAsync<TEntity>(
        IQueryable<TEntity> source,
        Expression<Func<TEntity, bool>> seekCondition,
        int pageSize,
        CancellationToken cancel) where TEntity : class
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (seekCondition is null)
        {
            throw new ArgumentNullException(nameof(seekCondition));
        }

        source = source
            .Where(seekCondition);

        return ToSeekBackListAsync(source, pageSize, hasNext: true, cancel);
    }
}