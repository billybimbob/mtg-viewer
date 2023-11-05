using EntityFrameworkCore.Paging;

namespace MtgViewer.Data.Infrastructure;

public readonly record struct SeekRequest<T>(
    T? Origin,
    SeekDirection Direction)
    where T : class;
