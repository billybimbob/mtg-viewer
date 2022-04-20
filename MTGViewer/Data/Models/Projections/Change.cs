using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data;

public sealed record RecentTransaction
{
    public DateTime AppliedAt { get; init; }
    public IEnumerable<RecentChange> Changes { get; init; } = Enumerable.Empty<RecentChange>();
    public int Copies { get; init; }

    public bool Equals(RecentTransaction? other)
    {
        return other is not null
            && other.AppliedAt == AppliedAt
            && other.Copies == Copies
            && other.Changes.SequenceEqual(Changes);
    }

    public override int GetHashCode()
    {
        return AppliedAt.GetHashCode()
            ^ Copies.GetHashCode()
            ^ Changes.Aggregate(0, (hash, c) => hash ^ c.GetHashCode());
    }
}

public sealed record RecentChange
{
    public bool ToStorage { get; init; }
    public bool FromStorage { get; init; }
    public string CardName { get; init; } = string.Empty;
}

public sealed record TransactionPreview
{
    public int Id { get; init; }
    public DateTime AppliedAt { get; init; }

    public int Copies { get; init; }
    public IEnumerable<LocationLink> Cards { get; init; } = Enumerable.Empty<LocationLink>();

    public bool HasMore => Copies > Cards.Sum(l => l.Held);

    public override int GetHashCode()
    {
        return Id.GetHashCode()
            ^ AppliedAt.GetHashCode()
            ^ Copies.GetHashCode()
            ^ Cards.Aggregate(0, (hash, c) => hash ^ c.GetHashCode());
    }

    public bool Equals(TransactionPreview? other)
    {
        return other is not null
            && other.Id == Id
            && other.AppliedAt == AppliedAt
            && other.Copies == Copies
            && other.Cards.SequenceEqual(Cards);
    }
}

public sealed record TransactionDetails
{
    public int Id { get; init; }

    public DateTime AppliedAt { get; init; }

    public int Copies { get; init; }

    public bool CanDelete { get; init; }
}

public sealed record ChangeDetails
{
    public int Id { get; init; }

    public MoveTarget To { get; init; } = default!;
    public MoveTarget? From { get; init; }

    public int Copies { get; init; }
    public CardPreview Card { get; init; } = default!;
}

public sealed record MoveTarget
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    internal LocationType Type { get; init; }
}

public sealed record Move
{
    public MoveTarget To { get; init; } = default!;
    public MoveTarget? From { get; init; }
    public IEnumerable<ChangeDetails> Changes { get; init; } = Enumerable.Empty<ChangeDetails>();

    public override int GetHashCode()
    {
        return To.GetHashCode()
            ^ From?.GetHashCode() ?? 0
            ^ Changes.Aggregate(0, (hash, c) => hash ^ c.GetHashCode());
    }

    public bool Equals(Move? other)
    {
        return other is not null
            && other.To == To
            && other.From == From
            && other.Changes.SequenceEqual(Changes);
    }
}
