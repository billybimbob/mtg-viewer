using System;

namespace EntityFrameworkCore.Paging;

public readonly record struct Seek
{
    public object? Previous { get; }
    public object? Next { get; }
    public bool IsPartial { get; }

    public Seek(object? previous, object? next, bool isPartial = false)
    {
        if ((previous, next, isPartial) is (not null, not null, true))
        {
            throw new ArgumentException("Seek cannot be partial if both previous and next are defined", nameof(isPartial));
        }

        Previous = previous;
        Next = next;
        IsPartial = isPartial;
    }
}

public readonly record struct Seek<T> where T : class
{
    public T? Previous { get; }
    public T? Next { get; }
    public bool IsPartial { get; }

    public Seek(T? previous, T? next, bool isPartial = false)
    {
        if ((previous, next, isPartial) is (not null, not null, true))
        {
            throw new ArgumentException("Seek cannot be partial if both previous and next are defined", nameof(isPartial));
        }

        Previous = previous;
        Next = next;
        IsPartial = isPartial;
    }

    public static explicit operator Seek(Seek<T> seek)
    {
        return new Seek(seek.Previous, seek.Next, seek.IsPartial);
    }
}
