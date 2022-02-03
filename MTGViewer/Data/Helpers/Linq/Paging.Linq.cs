using System.Collections.Generic;
using System.Collections.Paging;
using System.Threading;
using System.Threading.Tasks;

namespace System.Linq;

public static partial class PagingExtensions
{
    public static OffsetList<TEntity> ToOffsetList<TEntity>(
        this IEnumerable<TEntity> source,
        int pageSize, 
        int? pageIndex = null)
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
        int totalItems = source.Count();

        var items = source
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();

        var offset = new Offset(page, totalItems, pageSize);

        return new(offset, items);
    }


    public static async Task<OffsetList<TEntity>> ToOffsetListAsync<TEntity>(
        this IAsyncEnumerable<TEntity> source,
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


    public static IAsyncEnumerable<TSource[]> Chunk<TSource>(
        this IAsyncEnumerable<TSource> source,
        int size)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return CoreChunk(source, size);

        static async IAsyncEnumerable<TSource[]> CoreChunk(
            IAsyncEnumerable<TSource> source, 
            int size, 
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancel = default)
        {
            await using var e = source
                .WithCancellation(cancel)
                .ConfigureAwait(false)
                .GetAsyncEnumerator();

            while (await e.MoveNextAsync())
            {
                var chunk = new TSource[size];
                chunk[0] = e.Current;

                int i = 1;
                while (i < chunk.Length && await e.MoveNextAsync())
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

}