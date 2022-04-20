using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data;

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

public sealed record ExchangePreview
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool HasWants { get; init; }
    public IEnumerable<LocationCopy> Givebacks { get; init; } = Enumerable.Empty<LocationCopy>();

    public bool Equals(ExchangePreview? other)
    {
        return other is not null
            && other.Id == Id
            && other.Name == Name
            && other.Givebacks.SequenceEqual(Givebacks);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode()
            ^ Name.GetHashCode()
            ^ Givebacks.Aggregate(0, (hash, g) => hash ^ g.GetHashCode());
    }
}
