using System;
using System.Collections.Paging;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore;

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


    public static async Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, bool>> skip,
        int pageSize,
        TEntity? before,
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

        var items = await source
            .Where(skip)
            .Take(pageSize + 1)
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        var seek = new Seek<TEntity>(before, items.ElementAtOrDefault(^1));

        if (items.Any())
        {
            items.RemoveAt(items.Count - 1);
        }

        return new(seek, items);
    }


    public static async Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, bool>> skip,
        int pageSize,
        Task<TEntity?> before,
        CancellationToken cancel = default)
    {
        var beforeEntity = await before.ConfigureAwait(false);

        return await source
            .ToSeekListAsync(skip, pageSize, beforeEntity, cancel)
            .ConfigureAwait(false);
    }


    public static async Task<SeekList<TEntity>> ToSeekListAsync<TEntity>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, bool>> skip,
        int pageSize,
        ValueTask<TEntity?> before,
        CancellationToken cancel = default)
    {
        var beforeEntity = await before.ConfigureAwait(false);

        return await source
            .ToSeekListAsync(skip, pageSize, beforeEntity, cancel)
            .ConfigureAwait(false);
    }

}