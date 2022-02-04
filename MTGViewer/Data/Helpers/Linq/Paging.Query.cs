using System;
using System.Collections.Generic;
using System.Collections.Paging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore;


public enum SeekPosition
{
    Forward,
    Backwards,
    Start,
    End
}


public static partial class PagingExtensions
{
    public static async Task<OffsetList<TEntity>> ToOffsetListAsync<TEntity>(
        this IQueryable<TEntity> source,
        int pageSize, 
        int? pageIndex = null,
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
        int pageSize,
        SeekPosition position,
        CancellationToken cancel = default) where TEntity : class
    {
        return position switch
        {
            SeekPosition.Start => ToSeekListAsync(source, pageSize, false, cancel),
            SeekPosition.End => ToSeekBackListAsync(source, pageSize, false, cancel),

            SeekPosition.Backwards => ToSeekBackListAsync(source, pageSize, true, cancel),
            SeekPosition.Forward or _ => ToSeekListAsync(source, pageSize, true, cancel)
        };
    }


    private static async Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        IQueryable<TEntity> source,
        int pageSize,
        bool hasPrevious,
        CancellationToken cancel = default) where TEntity : class
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
        CancellationToken cancel = default) where TEntity : class
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


    private static Task<List<TEntity>> SeekBoundaryAsync<TEntity>(
        IQueryable<TEntity> source,
        int pageSize,
        CancellationToken cancel) where TEntity : class
    {
        return source
            .Select((Entity, Index) => new { Entity, Index })
            .Where(ei => ei.Index % pageSize == 0)
            .Select(ei => ei.Entity)
            .AsNoTracking()
            .ToListAsync(cancel);
    }
}