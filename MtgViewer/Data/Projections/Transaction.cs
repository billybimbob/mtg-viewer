using System;
using System.Collections.Generic;
using System.Linq;

namespace MtgViewer.Data.Projections;

public sealed class RecentTransaction
{
    public required DateTime AppliedAt { get; init; }

    public required IEnumerable<RecentChange> Changes { get; init; }

    public required int Copies { get; init; }
}

public sealed class TransactionPreview
{
    public required int Id { get; init; }

    public required DateTime AppliedAt { get; init; }

    public required int Copies { get; init; }

    public required IEnumerable<LocationLink> Cards { get; init; }

    public bool HasMore => Copies > Cards.Sum(l => l.Held);
}

public sealed record TransactionDetails
{
    public required int Id { get; init; }

    public required DateTime AppliedAt { get; init; }

    public required int Copies { get; init; }

    public required bool CanDelete { get; init; }

    public bool IsEmpty => Copies == 0;
}
