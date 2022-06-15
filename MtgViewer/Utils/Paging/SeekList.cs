using System;
using System.Collections;
using System.Collections.Generic;

namespace EntityFrameworkCore.Paging;

public enum SeekDirection
{
    Forward,
    Backwards
}

public readonly record struct Seek
{
    public object? Previous { get; }
    public object? Next { get; }
    public bool IsMissing { get; }

    public Seek(object origin, SeekDirection direction, bool isMissing)
    {
        ArgumentNullException.ThrowIfNull(origin);

        if (direction is SeekDirection.Forward)
        {
            Previous = origin;
            Next = null;
        }
        else
        {
            Previous = null;
            Next = origin;
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

public readonly record struct Seek<T> where T : class
{
    public T? Previous { get; }
    public T? Next { get; }
    public bool IsMissing { get; }

    public Seek(T origin, SeekDirection direction, bool isMissing)
    {
        ArgumentNullException.ThrowIfNull(origin);

        if (direction is SeekDirection.Forward)
        {
            Previous = origin;
            Next = null;
        }
        else
        {
            Previous = null;
            Next = origin;
        }

        IsMissing = isMissing;
    }

    public Seek(T previous, T next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);

        Previous = previous;
        Next = next;
        IsMissing = false;
    }

    public static explicit operator Seek(Seek<T> seek)
    {
        return (seek.Previous, seek.Next, seek.IsMissing) switch
        {
            (T p, T n, _) => new Seek(p, n),
            (T p, _, bool m) => new Seek(p, SeekDirection.Forward, m),
            (_, T n, bool m) => new Seek(n, SeekDirection.Backwards, m),
            _ => new Seek()
        };
    }
}

public class SeekList<T> : IReadOnlyList<T> where T : class
{
    private readonly IReadOnlyList<T> _items;

    internal SeekList()
    {
        Seek = new Seek<T>();
        _items = Array.Empty<T>();
    }

    public SeekList(IReadOnlyList<T> items, bool hasPrevious, bool hasNext, bool isMissing)
    {
        ArgumentNullException.ThrowIfNull(items);

        Seek = CreateSeek(items, hasPrevious, hasNext, isMissing);
        _items = items;
    }

    public SeekList(
        IReadOnlyList<T> items,
        SeekDirection direction,
        bool hasOrigin,
        bool lookAhead,
        int? targetSize)
    {
        ArgumentNullException.ThrowIfNull(items);

        Seek = CreateSeek(items, direction, hasOrigin, lookAhead, targetSize);
        _items = items;
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

        return (previous, next);
    }
}

public static class SeekList
{
    public static SeekList<T> Empty<T>() where T : class
        => Utils.EmptySeekList<T>.Value;
}
