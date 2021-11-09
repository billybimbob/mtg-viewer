using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

#nullable enable
namespace MTGViewer.Data;

public readonly struct Pages
{
    public int Current { get; }
    public int Total { get; }

    public bool HasPrevious => Current > 0;
    public bool HasNext => Current < Total - 1;
    public bool HasMultiple => Total > 1;


    public Pages(int currentPage, int totalPages)
    {
        Current = Math.Max(currentPage, 0);
        Total = Math.Max(totalPages, 0);
    }

    public Pages(int currentPage, int totalItems, int pageSize)
    {
        totalItems = Math.Max(totalItems, 0);
        pageSize = Math.Max(pageSize, 1);

        Current = Math.Max(currentPage, 0);
        Total = (int) Math.Ceiling((double) totalItems / pageSize);
    }
}



public class PagedList<T> : IReadOnlyList<T>
{
    private static readonly Lazy<PagedList<T>> _empty = new(() => 
        new PagedList<T>(default, new List<T>()) );

    private readonly List<T> _items;

    public PagedList(Pages page, List<T> items)
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


    public async static Task<PagedList<T>> ToPagedListAsync<T>(
        this IQueryable<T> source,
        int pageSize, int? pageIndex = null)
    {
        pageSize = Math.Max(pageSize, 0);

        int page = pageIndex ?? 0;
        int totalItems = await source.CountAsync();

        var pages = new Pages(page, totalItems, pageSize);

        var items = await source
            .Skip(pages.Current * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new(pages, items);
    }
}