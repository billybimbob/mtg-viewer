using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace System.Paging;


public enum SeekDirection
{
    Forward,
    Backwards
}


public readonly record struct Seek<T>(T? Previous, T? Next)
{
    public Seek(
        IReadOnlyList<T> items,
        SeekDirection direction,
        bool hasOrigin,
        bool lookAhead) : this(default, default)
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


public class SeekList<T> : IReadOnlyList<T>
{
    private readonly IReadOnlyList<T> _items;

    public SeekList(Seek<T> seek, IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items, nameof(items));

        Seek = seek;

        _items = items;
    }

    public Seek<T> Seek { get; }

    public int Count => _items.Count;

    public T this[int index] => _items[index];


    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();


    private static SeekList<T>? _empty;
    public static SeekList<T> Empty => _empty ??= new(default, Array.Empty<T>());
}
