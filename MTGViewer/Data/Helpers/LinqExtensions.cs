using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MTGViewer.Data;

public static class LinqExtensions
{
    public static PagedList<T> ToPagedList<T>(
        this IEnumerable<T> source,
        int pageSize, 
        int? pageIndex = null)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        pageSize = Math.Max(pageSize, 0);

        int page = pageIndex ?? 0;
        int totalItems = source.Count();

        var pages = new Pages(page, totalItems, pageSize);

        var items = source
            .Skip(pages.Current * pageSize)
            .Take(pageSize)
            .ToList();

        return new(pages, items);
    }


    public static async Task<PagedList<TEntity>> ToPagedListAsync<TEntity>(
        this IAsyncEnumerable<TEntity> source,
        int pageSize,
        int? pageIndex = null,
        CancellationToken cancel = default)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        pageSize = Math.Max(pageSize, 0);

        int page = pageIndex ?? 0;
        int totalItems = await source.CountAsync(cancel);

        var pages = new Pages(page, totalItems, pageSize);

        var items = await source
            .Skip(pages.Current * pageSize)
            .Take(pageSize)
            .ToListAsync(cancel);

        return new(pages, items);
    }


    public static async Task<PagedList<TEntity>> ToPagedListAsync<TEntity>(
        this IQueryable<TEntity> source,
        int pageSize, 
        int? pageIndex = null,
        CancellationToken cancel = default)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        pageSize = Math.Max(pageSize, 0);

        int page = pageIndex ?? 0;
        int totalItems = await source.CountAsync(cancel);

        var pages = new Pages(page, totalItems, pageSize);

        var items = await source
            .Skip(pages.Current * pageSize)
            .Take(pageSize)
            .ToListAsync(cancel);

        return new(pages, items);
    }


    public static async IAsyncEnumerable<TSource[]> Chunk<TSource>(
        this IAsyncEnumerable<TSource> source,
        int size,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancel = default)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        await using var e = source.GetAsyncEnumerator(cancel);

        while (await e.MoveNextAsync(cancel))
        {
            TSource[] chunk = new TSource[size];
            chunk[0] = e.Current;

            int i = 1;
            while (i < chunk.Length && await e.MoveNextAsync(cancel))
            {
                chunk[i++] = e.Current;
            }

            if (i == chunk.Length)
            {
                yield return chunk;
            }
            else
            {
                Array.Resize(ref chunk, i);
                yield return chunk;
                yield break;
            }
        }
    }
}