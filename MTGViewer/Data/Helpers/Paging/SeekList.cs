using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace System.Paging;


public enum SeekDirection
{
    Forward,
    Backwards
}


public readonly record struct Seek(object? Previous, object? Next);


public readonly record struct Seek<T>(T? Previous, T? Next)
{
    public Seek(
        IEnumerable<T> items,
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

    public static explicit operator Seek(Seek<T> seek)
    {
        return new Seek(seek.Previous, seek.Next);
    }
}


public class SeekList<T> : IReadOnlyList<T>
{
    private readonly IReadOnlyList<T> _items;

    public SeekList(Seek<T> seek, IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

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
