using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace System.Paging;


public enum SeekDirection
{
    Forward,
    Backwards
}


public readonly record struct Seek(object? Previous, object? Next)
{
    public Seek(
        IEnumerable items,
        SeekDirection direction,
        bool hasOrigin,
        bool lookAhead) : this(default, default)
    {
        if (direction is SeekDirection.Forward)
        {
            Previous = hasOrigin
                ? items.OfType<object>().FirstOrDefault()
                : default;

            Next = lookAhead
                ? items.OfType<object>().LastOrDefault()
                : default;
        }
        else
        {
            Previous = lookAhead
                ? items.OfType<object>().FirstOrDefault()
                : default;

            Next = hasOrigin
                ? items.OfType<object>().LastOrDefault()
                : default;
        }
    }
}


public class SeekList<TEntity> : IReadOnlyList<TEntity>
{
    private readonly IReadOnlyList<TEntity> _items;

    public SeekList(Seek seek, IReadOnlyList<TEntity> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Seek = seek;

        _items = items;
    }

    public Seek Seek { get; }

    public int Count => _items.Count;

    public TEntity this[int index] => _items[index];


    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<TEntity> GetEnumerator() => _items.GetEnumerator();


    private static SeekList<TEntity>? _empty;
    public static SeekList<TEntity> Empty => _empty ??= new(default, Array.Empty<TEntity>());
}
