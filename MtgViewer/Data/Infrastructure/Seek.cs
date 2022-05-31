using System;
using System.Paging;

namespace MtgViewer.Data.Infrastructure;

public readonly record struct SeekRequest<T>(T? Origin, SeekDirection Direction)
    where T : class;

public readonly record struct LoadedSeekList<T>(T? Origin, SeekDirection Direction, SeekList<T>? List)
    where T : class;

public readonly record struct SeekDto<T>(T? Previous, T? Next, bool IsMissing)
    where T : class
{
    public static explicit operator SeekDto<T>(Seek<T> seek)
    {
        return new SeekDto<T>(seek.Previous, seek.Next, seek.IsMissing);
    }

    public static explicit operator Seek<T>(SeekDto<T> dto)
    {
        return dto switch
        {
            (T p, T n, _) => new Seek<T>(p, n),
            (T p, null, bool m) => new Seek<T>(p, SeekDirection.Backwards, m),
            (null, T n, bool m) => new Seek<T>(n, SeekDirection.Forward, m),
            (null, null, false) => new Seek<T>(),
            _ => throw new InvalidCastException("Cannot convert to a valid seek")
        };
    }
}
