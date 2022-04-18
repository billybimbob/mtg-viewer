using System;
using System.Collections.Generic;
using System.Linq;
using MTGViewer.Data.Internal;

namespace MTGViewer.Data;

#region Card Projections

public sealed record HeldCard(Card Card, int Copies);

public sealed record CardImage
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
}

public record CardPreview
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    public string? ManaCost { get; init; }
    public float? ManaValue { get; init; }

    public string SetName { get; init; } = string.Empty;
    public Rarity Rarity { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
}

public record LocationCopy : CardPreview
{
    public int Held { get; init; }
}

public sealed record DeckCopy : LocationCopy
{
    public int Want { get; init; }
    public int Returning { get; init; }
}

public record CardLink
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string SetName { get; init; } = string.Empty;
    public string? ManaCost { get; init; } = string.Empty;
}

public sealed record DeleteLink : CardLink
{
    public bool HasDeckCopies { get; init; }
    public int StorageCopies { get; init; }
}

public record LocationLink : CardLink
{
    public int Held { get; init; }
}

public sealed record DeckLink : LocationLink
{
    public int Want { get; init; }
    public int Returning { get; init; }
}

public sealed record CardId
{
    public string Id { get; init; } = string.Empty;
    public string MultiverseId { get; init; } = string.Empty;
}

#endregion

public sealed class Statistics
{
    public IReadOnlyDictionary<Rarity, int> Rarities { get; init; } = default!;
    public IReadOnlyDictionary<Color, int> Colors { get; init; } = default!;

    public IReadOnlyDictionary<string, int> Types { get; init; } = default!;
    public IReadOnlyDictionary<int, int> ManaValues { get; init; } = default!;

    // either rarity or mana value sum could be used
    public int Copies => Rarities.Values.Sum();

    public float ManaValueAvg =>
        ManaValues.Sum(kv => kv.Key * (float)kv.Value) / Copies;
}

#region Change Projections

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

#endregion

#region TheoryCraft Projections

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

#endregion

#region Trade/Suggestion Projections

public sealed record TradeDeckPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Color Color { get; init; }

    public bool SentTrades { get; init; }
    public bool ReceivedTrades { get; init; }
    public bool WantsCards { get; init; }
}

public sealed record SuggestionPreview
{
    public int Id { get; init; }
    public DateTime SentAt { get; init; }

    public string CardId { get; init; } = string.Empty;
    public string CardName { get; init; } = string.Empty;
    public string? CardManaCost { get; init; }

    public string? ToName { get; init; }
    public string? Comment { get; init; }
}

public sealed record TradePreview
{
    public int Id { get; init; }
    public CardPreview Card { get; init; } = default!;
    public DeckDetails Target { get; init; } = default!;
    public int Copies { get; init; }
}

#endregion

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

public sealed record QuantityPreview
{
    public int Id { get; init; }
    public CardPreview Card { get; init; } = default!;
    public int Copies { get; init; }
}

#region Box Projections

public sealed class BoxPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;

    public BinPreview Bin { get; init; } = default!;

    public string? Appearance { get; init; }
    public int Capacity { get; init; }
    public int Held { get; init; }

    public IEnumerable<LocationLink> Cards { get; init; } = Enumerable.Empty<LocationLink>();

    public bool HasMoreCards => Held > Cards.Sum(s => s.Held);
}

public sealed record BinPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public IEnumerable<BoxPreview> Boxes { get; init; } = Enumerable.Empty<BoxPreview>();

    public bool Equals(BinPreview? other)
    {
        return other is not null
            && Id == other.Id
            && Name == other.Name
            && Boxes.SequenceEqual(other.Boxes);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode()
            ^ Name.GetHashCode()
            ^ Boxes.Aggregate(0, (hash, c) => hash ^ c.GetHashCode());
    }
}

#endregion

public sealed record UserPreview
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}
