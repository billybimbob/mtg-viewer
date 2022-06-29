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
