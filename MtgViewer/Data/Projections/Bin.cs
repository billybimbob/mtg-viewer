using System;
using System.Collections.Generic;
using System.Linq;

namespace MtgViewer.Data.Projections;

public sealed record BinPreview
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public IEnumerable<BoxPreview> Boxes { get; init; } = Enumerable.Empty<BoxPreview>();

    public bool Equals(BinPreview? other)
    {
        return other is not null
            && Id == other.Id
            && Name == other.Name;
    }

    public override int GetHashCode() => HashCode.Combine(Id, Name);
}
