using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace System.Linq;

public static class LinqExtensions
{
    public static IAsyncEnumerable<TSource[]> Chunk<TSource>(
        this IAsyncEnumerable<TSource> source,
        int size)
    {
        ArgumentNullException.ThrowIfNull(source);

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