using System;
using System.Paging;

namespace MTGViewer.Utils;

public readonly record struct SeekRequest<T>(T? Seek, SeekDirection Direction);

public readonly record struct LoadedSeekList<T>(T? Seek, SeekDirection Direction, SeekList<T>? List)
    where T : class
{
    public bool IsUnfinished(int targetSize)
    {
        return (List?.Count < targetSize || List is null)
            && Seek is not null
            && (Direction is SeekDirection.Backwards && List?.Seek.Previous is null)
                || (Direction is SeekDirection.Forward && List?.Seek.Next is null);
    }
}

public static class SeekListExtensions
{
    public static bool IsUnfinished<TEntity, TSeek>(
        this SeekList<TEntity> list,
        TSeek? seek,
        SeekDirection direction,
        int targetSize)
        where TEntity : class
        where TSeek : class
    {
        ArgumentNullException.ThrowIfNull(list);

        return list.Count < targetSize
            && seek is not null
            && (direction is SeekDirection.Backwards && list.Seek.Previous is null
                || direction is SeekDirection.Forward && list.Seek.Next is null);
    }

    public static bool IsUnfinished<TEntity, TSeek>(
        this SeekList<TEntity> list,
        TSeek? seek,
        SeekDirection direction,
        int targetSize)
        where TEntity : class
        where TSeek : struct
    {
        ArgumentNullException.ThrowIfNull(list);

        return list.Count < targetSize
            && seek is not null
            && (direction is SeekDirection.Backwards && list.Seek.Previous is null
                || direction is SeekDirection.Forward && list.Seek.Next is null);
    }
}

