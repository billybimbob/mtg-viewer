using System.Collections.Generic;

namespace MtgViewer.Data.Projections;

public enum BuildState
{
    Theorycraft,
    Built,
    Requesting
}

public sealed record DeckPreview
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required Color Color { get; init; }

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

public sealed record DeckDetails : TheorycraftDetails
{
    public required PlayerPreview Owner { get; init; }
    public int ReturnCopies { get; init; }
    public bool HasTrades { get; init; }
}

public sealed class ExchangePreview
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required bool HasWants { get; init; }
    public required IEnumerable<CardCopy> Givebacks { get; init; }
}

public sealed record DeckCounts
{
    public int Id { get; init; }
    public required string OwnerId { get; init; }

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
    public required string Name { get; init; }

    public required IReadOnlyList<DeckCopy> Cards { get; init; }
}
