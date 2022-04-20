using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data;

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

public sealed record BinPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public IEnumerable<BoxPreview> Boxes { get; init; } = Enumerable.Empty<BoxPreview>();

    public bool Equals(BinPreview? other)
    {
        return other is not null
            && Id == other.Id
            && Name == other.Name
            && Boxes.SequenceEqual(other.Boxes);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode()
            ^ Name.GetHashCode()
            ^ Boxes.Aggregate(0, (hash, c) => hash ^ c.GetHashCode());
    }
}
