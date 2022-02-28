using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace System.Paging;


public enum SeekDirection
{
    Forward,
    Backwards
}


public readonly record struct Seek<TKey>(TKey? Previous, TKey? Next)
    where TKey : IEquatable<TKey>
{
    public Seek(
        IEnumerable<TKey> items,
        SeekDirection direction,
        bool hasOrigin,
        bool lookAhead) : this(default, default)
    {
        if (direction is SeekDirection.Forward)
        {
            Previous = hasOrigin
                ? items.FirstOrDefault()
                : default;

            Next = lookAhead
                ? items.LastOrDefault()
                : default;
        }
        else
        {
            Previous = lookAhead
                ? items.FirstOrDefault()
                : default;

            Next = hasOrigin
                ? items.LastOrDefault()
                : default;
        }
    }
}


public class SeekList<TEntity, TKey> : IReadOnlyList<TEntity>
    where TKey : IEquatable<TKey>
{
    private readonly IReadOnlyList<TEntity> _items;

    public SeekList(Seek<TKey> seek, IReadOnlyList<TEntity> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Seek = seek;

        _items = items;
    }

    public Seek<TKey> Seek { get; }

    public int Count => _items.Count;

    public TEntity this[int index] => _items[index];


    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<TEntity> GetEnumerator() => _items.GetEnumerator();


    private static SeekList<TEntity, TKey>? _empty;
    public static SeekList<TEntity, TKey> Empty => _empty ??= new(default, Array.Empty<TEntity>());
}
