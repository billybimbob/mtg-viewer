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

    public SeekList(
        IReadOnlyList<T> items,
        bool hasPrevious,
        bool hasNext,
        bool isMissing)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items = items;
        Seek = CreateSeek(items, hasPrevious, hasNext, isMissing);
    }

    public SeekList(
        IReadOnlyList<T> items,
        SeekDirection direction,
        bool hasOrigin,
        bool lookAhead,
        int? targetSize)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items = items;
        Seek = CreateSeek(items, direction, hasOrigin, lookAhead, targetSize);
    }

    public Seek<T> Seek { get; }

    public int Count => _items.Count;

    public T this[int index] => _items[index];

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    private static Seek<T> CreateSeek(
        IReadOnlyList<T> items,
        bool hasPrevious,
        bool hasNext,
        bool isMissing)
    {
        var previous = hasPrevious ? items[0] : default;
        var next = hasNext ? items[^1] : default;

        return CreateSeek(previous, next, isMissing);
    }

    private static Seek<T> CreateSeek(
        IReadOnlyList<T> items,
        SeekDirection direction,
        bool hasOrigin,
        bool lookAhead,
        int? targetSize)
    {
        var (previous, next) = GetSeekReferences(items, direction, hasOrigin, lookAhead);

        return CreateSeek(previous, next, items.Count < targetSize);
    }

    private static Seek<T> CreateSeek(T? previous, T? next, bool isMissing)
    {
        return (previous, next, isMissing) switch
        {
            (T p, T n, _) => new Seek<T>(p, n),
            (T p, _, bool m) => new Seek<T>(p, SeekDirection.Forward, m),
            (_, T n, bool m) => new Seek<T>(n, SeekDirection.Backwards, m),
            _ => new Seek<T>()
        };
    }

    private static (T?, T?) GetSeekReferences(
        IReadOnlyList<T> items,
        SeekDirection direction,
        bool hasOrigin,
        bool lookAhead)
    {
        var previous = (items, direction, hasOrigin, lookAhead) switch
        {
            ({ Count: > 0 }, SeekDirection.Forward, true, _) => items[0],
            ({ Count: > 0 }, SeekDirection.Backwards, _, true) => items[0],
            _ => default
        };

        var next = (items, direction, hasOrigin, lookAhead) switch
        {
            ({ Count: > 0 }, SeekDirection.Forward, _, true) => items[^1],
            ({ Count: > 0 }, SeekDirection.Backwards, true, _) => items[^1],
            _ => default
        };

        return (previous, next);
    }
}

public static class SeekList
{
    public static SeekList<T> Empty<T>() where T : class
        => SeekList<T>.Empty;
}
