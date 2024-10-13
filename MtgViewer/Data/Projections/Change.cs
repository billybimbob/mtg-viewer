using System.Collections.Generic;
using System.Linq;

namespace MtgViewer.Data.Projections;

public sealed record RecentChange
{
    public bool ToStorage { get; init; }

    public bool FromStorage { get; init; }

    public string CardName { get; init; } = string.Empty;
}

public sealed record ChangeDetails
{
    public int Id { get; init; }

    public LocationPreview To { get; init; } = default!;

    public LocationPreview? From { get; init; }

    public CardPreview Card { get; init; } = default!;

    public int Copies { get; init; }
}

public sealed record ChangePreview
{
    public TransactionDetails Transaction { get; init; } = default!;

    public string To { get; init; } = string.Empty;

    public string? From { get; init; }

    public string CardName { get; init; } = string.Empty;

    public int Copies { get; init; }
}

public sealed class Move
{
    public LocationPreview To { get; init; } = default!;

    public LocationPreview? From { get; init; }

    public IEnumerable<ChangeDetails> Changes { get; init; } = Enumerable.Empty<ChangeDetails>();
}
