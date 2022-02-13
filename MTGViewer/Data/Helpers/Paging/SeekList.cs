using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace System.Paging;

public enum SeekDirection
{
    Forward,
    Backwards
}


public readonly record struct Seek<TEntity>(int Index, TEntity? Previous, TEntity? Next)
{
    private readonly int _index = Math.Max(Index, 0);
    public int Index => _index;

    public Seek(int index, bool hasNext, IReadOnlyList<TEntity> items)
        : this(index, default(TEntity), default(TEntity))
    {
        Previous = index > 0
            ? items.ElementAtOrDefault(0)
            : default;

        Next = hasNext
            ? items.ElementAtOrDefault(^1)
            : default;
    }

    public Seek(bool hasPrevious, int index, IReadOnlyList<TEntity> items)
        : this(index, default(TEntity), default(TEntity))
    {
        Previous = hasPrevious
            ? items.ElementAtOrDefault(0)
            : default;

        Next = items.ElementAtOrDefault(^1);
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