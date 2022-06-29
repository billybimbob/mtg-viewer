using System;

namespace EntityFrameworkCore.Paging;

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
