using System;
using System.Collections.Generic;
using System.Linq;

namespace MtgViewer.Data.Projections;

public sealed class BinPreview
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public IEnumerable<BoxPreview> Boxes { get; init; } = Enumerable.Empty<BoxPreview>();

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Id);
        hash.Add(Name);

        // keep eye on, is O(N) runtime hash

        foreach (var box in Boxes)
        {
            hash.Add(box);
        }

        return hash.ToHashCode();
    }

    public override bool Equals(object? obj)
    {
        return obj is BinPreview other
            && Id == other.Id
            && Name == other.Name
            && Boxes.SequenceEqual(other.Boxes);
    }
}
