using EntityFrameworkCore.Paging;

namespace MtgViewer.Data.Infrastructure;

public readonly record struct SeekRequest<T>(
    T? Origin,
    SeekDirection Direction)
    where T : class;

public readonly record struct SeekDto(
    bool HasPrevious,
    bool HasNext,
    bool IsPartial)
{
    public static SeekDto From<T>(Seek<T> seek) where T : class
    {
        return new SeekDto(
            seek.Previous is not null,
            seek.Next is not null,
            seek.IsPartial);
    }
}
