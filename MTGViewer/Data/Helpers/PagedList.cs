using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;


namespace MTGViewer.Data
{
    public readonly struct Pages
    {
        public int Current { get; }
        public int Total { get; }

        public bool HasPrevious => Current > 0;
        public bool HasNext => Current < Total - 1;
        public bool HasMultiple => Total > 1;


        public Pages(int currentPage, int totalPages)
        {
            Current = currentPage;
            Total = totalPages;
        }

        public Pages(int currentPage, int totalItems, int pageSize)
        {
            Current = currentPage;
            Total = (int) Math.Ceiling((double) totalItems / pageSize);
        }
    }



    public class PagedList<T> : IReadOnlyList<T>
    {
        private readonly List<T> _items;

        public PagedList(Pages page, List<T> items)
        {
            Pages = page;
            _items = items;
        }

        public Pages Pages { get; }

        public T this[int index] => _items[index];
        public int Count => _items.Count;


        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    }



    public static class PagingLinqExtensions
    {
        public static PagedList<T> ToPagedList<T>(
            this IEnumerable<T> source,
            int pageSize, int? pageIndex = null)
        {
            int page = pageIndex ?? 0;
            int totalItems = source.Count();

            var pages = new Pages(page, totalItems, pageSize);

            var items = source
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToList();

            return new(pages, items);
        }


        public async static Task<PagedList<T>> ToPagedListAsync<T>(
            this IQueryable<T> source,
            int pageSize, int? pageIndex = null)
        {
            int page = pageIndex ?? 0;
            int totalItems = await source.CountAsync();

            var pages = new Pages(page, totalItems, pageSize);

            var items = await source
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new(pages, items);
        }
    }
}