using System;
using System.Collections.Generic;
using System.Linq;

namespace MtgViewer.Data.Projections;

public sealed class RecentTransaction
{
    public DateTime AppliedAt { get; init; }
    public IEnumerable<RecentChange> Changes { get; init; } = Enumerable.Empty<RecentChange>();
    public int Copies { get; init; }
}

public sealed record RecentChange
{
    public bool ToStorage { get; init; }
    public bool FromStorage { get; init; }
    public string CardName { get; init; } = string.Empty;
}

public sealed class TransactionPreview
{
    public int Id { get; init; }
    public DateTime AppliedAt { get; init; }

    public int Copies { get; init; }
    public IEnumerable<LocationLink> Cards { get; init; } = Enumerable.Empty<LocationLink>();

    public bool HasMore => Copies > Cards.Sum(l => l.Held);
}

public sealed record TransactionDetails
{
    public int Id { get; init; }
    public DateTime AppliedAt { get; init; }

    public int Copies { get; init; }
    public bool CanDelete { get; init; }
    public bool IsEmpty => Copies == 0;
}

public sealed record ChangeDetails
{
    public int Id { get; init; }

    public LocationPreview To { get; init; } = default!;
    public LocationPreview? From { get; init; }

    public int Copies { get; init; }
    public CardPreview Card { get; init; } = default!;
}

public sealed class Move
{
    public LocationPreview To { get; init; } = default!;
    public LocationPreview? From { get; init; }
    public IEnumerable<ChangeDetails> Changes { get; init; } = Enumerable.Empty<ChangeDetails>();
}
