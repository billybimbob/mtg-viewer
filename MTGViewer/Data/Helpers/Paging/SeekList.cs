using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace System.Paging;


public enum SeekDirection
{
    Forward,
    Backwards
}


public readonly record struct Seek<TEntity>(TEntity? Previous, TEntity? Next)
{
    public Seek(IReadOnlyList<TEntity> items, SeekDirection direction, bool hasOrigin, bool lookAhead) : this(default, default)
    {
        if (direction is SeekDirection.Forward)
        {
            Previous = hasOrigin
                ? items.ElementAtOrDefault(0)
                : default;

            Next = lookAhead
                ? items.ElementAtOrDefault(^1)
                : default;
        }
        else
        {
            Previous = lookAhead
                ? items.ElementAtOrDefault(0)
                : default;

            Next = hasOrigin
                ? items.ElementAtOrDefault(^1)
                : default;
        }
    }
}


public class SeekList<TEntity> : IReadOnlyList<TEntity>
{
    private static readonly SeekList<TEntity> _empty = new(default, Array.Empty<TEntity>());

    private readonly IList<TEntity> _items;

    public SeekList(Seek<TEntity> seek, IList<TEntity> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        Seek = seek;
        _items = items;
    }

    public Seek<TEntity> Seek { get; }

    public int Count => _items.Count;

    public TEntity this[int index] => _items[index];


    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<TEntity> GetEnumerator() => _items.GetEnumerator();

    public static SeekList<TEntity> Empty() => _empty;
}
