using System.Collections.Generic;

namespace MtgViewer.Data.Projections;

public sealed record RecentChange
{
    public required bool ToStorage { get; init; }

    public required bool FromStorage { get; init; }

    public required string CardName { get; init; }
}

public sealed record ChangeDetails
{
    public required int Id { get; init; }

    public required LocationPreview To { get; init; }

    public required LocationPreview? From { get; init; }

    public required CardPreview Card { get; init; }

    public required int Copies { get; init; }
}

public sealed class Move
{
    public required LocationPreview To { get; init; }

    public required LocationPreview? From { get; init; }

    public required IEnumerable<ChangeDetails> Changes { get; init; }
}
