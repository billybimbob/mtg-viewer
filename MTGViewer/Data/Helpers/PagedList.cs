using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MTGViewer.Data;


public readonly record struct Pages(int currentPage, int totalPages)
{
    public int Current { get; } = Math.Max(currentPage, 0);
    public int Total { get; } = Math.Max(totalPages, 0);

    public bool HasPrevious => Current > 0;
    public bool HasNext => Current < Total - 1;
    public bool HasMultiple => Total > 1;

    public Pages(int currentPage, int totalItems, int pageSize) 
        : this(currentPage, TotalPages(totalItems, pageSize))
    { }

    private static int TotalPages(int totalItems, int pageSize)
    {
        totalItems = Math.Max(totalItems, 0);
        pageSize = Math.Max(pageSize, 1);

        return (int) Math.Ceiling((double) totalItems / pageSize);
    }
}



public class PagedList<T> : IReadOnlyList<T>
{
    private static readonly Lazy<PagedList<T>> _empty = new(() => 
        new PagedList<T>(default, Array.Empty<T>()) );

    private readonly IList<T> _items;

    public PagedList(Pages page, IList<T> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException("Items is null");
        }

        Pages = page;
        _items = items;
    }

    public static PagedList<T> Empty => _empty.Value;

    public Pages Pages { get; }

    public int Count => _items.Count;

    public T this[int index] => _items[index];


    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
}



public static class PagingLinqExtensions
{
    public static PagedList<T> ToPagedList<T>(
        this IEnumerable<T> source,
        int pageSize, int? pageIndex = null)
    {
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


    public async static Task<PagedList<TEntity>> ToPagedListAsync<TEntity>(
        this IQueryable<TEntity> source,
        int pageSize, 
        int? pageIndex = null,
        CancellationToken cancel = default)
    {
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
}