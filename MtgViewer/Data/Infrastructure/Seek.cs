using EntityFrameworkCore.Paging;

namespace MtgViewer.Data.Infrastructure;

public readonly record struct SeekRequest<T>(T? Origin, SeekDirection Direction)
    where T : class;

public readonly record struct SeekDto<T>(bool HasPrevious, bool HasNext, bool IsMissing)
    where T : class
{
    public static explicit operator SeekDto<T>(Seek<T> seek)
    {
        return new SeekDto<T>(
            seek.Previous is not null,
            seek.Next is not null,
            seek.IsMissing);
    }
}

public readonly record struct LoadedSeekList<T>(T? Origin, SeekDirection Direction, SeekList<T>? List)
    where T : class;
