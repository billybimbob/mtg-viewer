using System.Collections.Generic;
using System.Linq;

namespace MtgViewer.Data.Projections;

public sealed class BoxPreview
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required BinPreview Bin { get; init; }

    public required string? Appearance { get; init; }

    public required int Capacity { get; init; }

    public required int Held { get; init; }

    public IEnumerable<LocationLink> Cards { get; init; } = Enumerable.Empty<LocationLink>();

    public bool HasMoreCards => Held > Cards.Sum(s => s.Held);
}
