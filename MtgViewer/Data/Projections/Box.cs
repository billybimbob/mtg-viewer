using System.Collections.Generic;
using System.Linq;

namespace MtgViewer.Data.Projections;

public sealed class BoxPreview
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public BinPreview Bin { get; init; } = default!;

    public string? Appearance { get; init; }

    public int Capacity { get; init; }

    public int Held { get; init; }

    public IEnumerable<LocationLink> Cards { get; init; } = Enumerable.Empty<LocationLink>();

    public bool HasMoreCards => Held > Cards.Sum(s => s.Held);
}
