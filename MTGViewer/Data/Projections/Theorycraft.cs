using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Projections;

public enum BuildState
{
    Theorycraft,
    Built,
    Requesting
}

public sealed record DeckPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Color Color { get; init; }

    public int HeldCopies { get; init; }
    public int WantCopies { get; init; }

    public bool HasReturns { get; init; }
    public bool HasTrades { get; init; }

    public BuildState BuildState => this switch
    {
        { HasTrades: true } => BuildState.Requesting,
        { WantCopies: > 0 } or { HasReturns: true } => BuildState.Theorycraft,
        _ => BuildState.Built
    };
}

public sealed class TheoryColors
{
    public int Id { get; init; }
    public IEnumerable<Color> HoldColors { get; init; } = Enumerable.Empty<Color>();
    public IEnumerable<Color> WantColors { get; init; } = Enumerable.Empty<Color>();
    public IEnumerable<Color> SideboardColors { get; init; } = Enumerable.Empty<Color>();
}

public sealed record DeckDetails
{
    public int Id { get; init; }
    public OwnerPreview Owner { get; init; } = default!;

    public string Name { get; init; } = string.Empty;
    public Color Color { get; init; }

    public int HeldCopies { get; init; }
    public int WantCopies { get; init; }
    public int ReturnCopies { get; init; }

    public bool HasTrades { get; init; }
}

public sealed record OwnerPreview
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

public sealed record UnclaimedDetails
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Color Color { get; init; }

    public int HeldCopies { get; init; }
    public int WantCopies { get; init; }
}

public sealed class ExchangePreview
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool HasWants { get; init; }
    public IEnumerable<LocationCopy> Givebacks { get; init; } = Enumerable.Empty<LocationCopy>();
}

public sealed record DeckCounts
{
    public int Id { get; init; }
    public string OwnerId { get; init; } = string.Empty;

    public int HeldCopies { get; init; }
    public int WantCopies { get; set; }
    public int ReturnCopies { get; set; }

    public int HeldCount { get; init; }
    public int WantCount { get; set; }
    public int ReturnCount { get; set; }

    public bool HasTrades { get; init; }
}

public sealed class MulliganTarget
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<DeckCopy> Cards { get; init; } = Array.Empty<DeckCopy>();
}
