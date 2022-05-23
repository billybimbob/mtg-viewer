using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace System.Paging;

public enum SeekDirection
{
    Forward,
    Backwards
}

public readonly struct Seek
{
    public object? Previous { get; }
    public object? Next { get; }
    public bool IsMissing { get; }

    public Seek(object reference, SeekDirection direction, bool isMissing)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (direction is SeekDirection.Forward)
        {
            Previous = null;
            Next = reference;
        }
        else
        {
            Previous = reference;
            Next = null;
        }

        IsMissing = isMissing;
    }

    public Seek(object previous, object next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);

        Previous = previous;
        Next = next;
        IsMissing = false;
    }
}

public readonly struct Seek<T>
{
    public T? Previous { get; }
    public T? Next { get; }
    public bool IsMissing { get; }

    public Seek(T reference, SeekDirection direction, bool isMissing)
    {
        if (direction is SeekDirection.Forward)
        {
            Previous = default;
            Next = reference;
        }
        else
        {
            Previous = reference;
            Next = default;
        }

        IsMissing = isMissing;
    }

    public Seek(T previous, T next)
    {
        Previous = previous;
        Next = next;
        IsMissing = false;
    }

    public Seek(
        IReadOnlyList<T> items,
        SeekDirection direction,
        bool hasOrigin,
        int? targetSize,
        bool lookAhead)
    {
        if (direction is SeekDirection.Forward)
        {
            Previous = hasOrigin && items.Any() ? items[0] : default;
            Next = lookAhead && items.Any() ? items[^1] : default;
        }
        else
        {
            Previous = lookAhead && items.Any() ? items[0] : default;
            Next = hasOrigin && items.Any() ? items[^1] : default;
        }

        IsMissing = items.Count < targetSize && (Previous is null ^ Next is null);
    }

    public static explicit operator Seek(Seek<T> seek)
    {
        return (seek.Previous, seek.Next, seek.IsMissing) switch
        {
            (T p, T n, _) => new Seek(p, n),
            (_, T n, bool m) => new Seek(n, SeekDirection.Forward, m),
            (T p, _, bool m) => new Seek(p, SeekDirection.Backwards, m),
            _ => new Seek()
        };
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
