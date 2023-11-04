using System;
using System.Collections;
using System.Collections.Generic;

namespace EntityFrameworkCore.Paging;

public class SeekList<T> : IReadOnlyList<T> where T : class
{
    internal static SeekList<T> Empty { get; } = new();

    private readonly IReadOnlyList<T> _items;

    public SeekList()
    {
        _items = Array.Empty<T>();
        Seek = new Seek<T>();
    }

    public SeekList(IReadOnlyList<T> items, bool hasPrevious, bool hasNext, bool isPartial)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count is 0 && (hasPrevious || hasNext))
        {
            throw new ArgumentException("Items is expected to not be empty", nameof(items));
        }

        _items = items;

        var previous = hasPrevious ? items[0] : default;
        var next = hasNext ? items[^1] : default;

        Seek = new Seek<T>(previous, next, isPartial);
    }

    public SeekList(IReadOnlyList<T> items, SeekDirection direction, bool hasOrigin, bool lookAhead, int? targetSize)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count is 0 && (hasOrigin || lookAhead))
        {
            throw new ArgumentException("Items is expected to not be empty", nameof(items));
        }

        _items = items;

        var previous = (direction, hasOrigin, lookAhead) switch
        {
            (SeekDirection.Forward, true, _) => items[0],
            (SeekDirection.Backwards, _, true) => items[0],
            _ => default
        };

        var next = (direction, hasOrigin, lookAhead) switch
        {
            (SeekDirection.Forward, _, true) => items[^1],
            (SeekDirection.Backwards, true, _) => items[^1],
            _ => default
        };

        Seek = new Seek<T>(previous, next, items.Count < targetSize);
    }

    public Seek<T> Seek { get; }

    public int Count => _items.Count;

    public T this[int index] => _items[index];

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
}

public static class SeekList
{
    public static SeekList<T> Empty<T>() where T : class
        => SeekList<T>.Empty;
}
