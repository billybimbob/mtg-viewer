using System.Collections.Generic;
using System.Linq;

namespace MtgViewer.Data.Projections;

public sealed class TheoryColors
{
    public int Id { get; init; }

    public IEnumerable<Color> HoldColors { get; init; } = Enumerable.Empty<Color>();

    public IEnumerable<Color> WantColors { get; init; } = Enumerable.Empty<Color>();

    public IEnumerable<Color> SideboardColors { get; init; } = Enumerable.Empty<Color>();
}

public record TheorycraftDetails
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public Color Color { get; init; }

    public int HeldCopies { get; init; }

    public int WantCopies { get; init; }
}
