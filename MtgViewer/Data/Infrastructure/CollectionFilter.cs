using EntityFrameworkCore.Paging;

namespace MtgViewer.Data.Infrastructure;

public sealed class CollectionFilter
{
    public string? Search { get; set; }

    public Color Colors { get; set; }

    public string? Order { get; set; }

    public string? Seek { get; set; }

    public SeekDirection Direction { get; set; }
}
