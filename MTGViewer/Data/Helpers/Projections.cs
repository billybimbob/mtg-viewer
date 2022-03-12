using System;
using System.Collections.Generic;
using System.Linq;
using MTGViewer.Data.Internal;

namespace MTGViewer.Data;


#region Card Projections

public sealed record CardTotal(Card Card, int Total);


public record CardImage
{
    public string Id { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string ImageUrl { get; init; } = default!;
}


public record CardPreview : CardImage
{
    public string? ManaCost { get; init; }
    public string SetName { get; init; } = default!;
    public Rarity Rarity { get; init; }
}


public record CardCopies : CardPreview
{
    public int Copies { get; init; }
}


public sealed record DeckCopies : CardPreview
{
    public int Held { get; init; }
    public int Want { get; init; }
    public int Returning { get; init; }
}


public record CardLink
{
    public string Id { get; init; } = default!;
    public string Name { get; init; } = default!;

    public string SetName { get; init; } = default!;
    public string? ManaCost { get; init; } = default!;
}


public sealed record StorageLink : CardLink
{
    public int Copies { get; init; }
}


public sealed record DeckLink : CardLink
{
    public int Held { get; init; }
    public int Want { get; init; }
    public int Returning { get; init; }
}


public sealed record CardId
{
    public string Id { get; init; } = default!;
    public string MultiverseId { get; init; } = default!;
}

#endregion



#region Change Projections

public sealed class RecentTransaction
{
    public DateTime AppliedAt { get; init; }
    public IEnumerable<RecentChange> Changes { get; init; } = Enumerable.Empty<RecentChange>();
    public int Total { get; init; }
}


public sealed record RecentChange
{
    public bool ToStorage { get; init; }
    public bool FromStorage { get; init; }
    public string CardName { get; init; } = default!;
}


public sealed record TransferPreview(
    TransactionPreview Transaction,
    LocationPreview To,
    LocationPreview? From,
    IReadOnlyList<ChangePreview> Changes)
{
    public bool Equals(TransferPreview? transfer)
    {
        return transfer is var (transaction, to, from, changes)
            && transaction == Transaction
            && to == To
            && from == From
            && changes.SequenceEqual(Changes);
    }

    public override int GetHashCode()
    {
        return Transaction.GetHashCode()
            ^ To.GetHashCode()
            ^ From?.GetHashCode() ?? 0
            ^ Changes.Aggregate(0, (hash, c) => hash ^ c.GetHashCode());
    }
}


public sealed record TransactionPreview
{
    public int Id { get; init; }
    public DateTime AppliedAt { get; init; }
    public bool IsShared { get; init; }
}


public sealed record ChangePreview
{
    public int Id { get; init; }
    public int Amount { get; init; }
    public TransactionPreview Transaction { get; init; } = default!;

    public LocationPreview To { get; init; } = default!;
    public LocationPreview? From { get; init; }

    public CardPreview Card { get; init; } = default!;
}


public sealed record LocationPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    internal LocationType Type { get; init; }
}

#endregion



#region Deck Projections

public enum BuildState
{
    Theorycraft,
    Built,
    Requesting
}


public sealed record DeckPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public Color Color { get; init; }
    public int CardTotal { get; init; }

    public bool HasWants { get; init; }
    public bool HasReturns { get; init; }
    public bool HasTradesTo { get; init; }

    public BuildState BuildState => this switch
    {
        { HasTradesTo: true } => BuildState.Requesting,
        { HasWants: true } or { HasReturns: true } => BuildState.Theorycraft,
        _ => BuildState.Built
    };
}


public sealed class DeckColors
{
    public int Id { get; init; }
    public IEnumerable<Color> CardColors { get; init; } = Enumerable.Empty<Color>();
    public IEnumerable<Color> WantColors { get; init; } = Enumerable.Empty<Color>();
}


public sealed record DeckDetails
{
    public int Id { get; init; }
    public OwnerPreview Owner { get; init; } = default!;

    public string Name { get; init; } = default!;
    public Color Color { get; init; } = default!;

    public int AmountTotal { get; init; }
    public int WantTotal { get; init; }
    public int GiveBackTotal { get; init; }

    public bool AnyTrades { get; init; }
}


public sealed record OwnerPreview
{
    public string Id { get; init; } = default!;
    public string Name { get; init; } = default!;
}


public sealed record DeckTradePreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public Color Color { get; init; }

    public bool SentTrades { get; init; }
    public bool ReceivedTrades { get; init; }
    public bool WantsCards { get; init; }
}


public sealed record SuggestionPreview
{
    public int Id { get; init; }
    public DateTime SentAt { get; init; }

    public string CardId { get; init; } = default!;
    public string CardName { get; init; } = default!;
    public string? CardManaCost { get; init; }

    public string? ToName { get; init; }
    public string? Comment { get; init; }
}

#endregion



#region Box Projections

public sealed class BoxPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;

    public BinPreview Bin { get; init; } = default!;

    public int Capacity { get; init; }
    public int TotalCards { get; init; }

    public IEnumerable<StorageLink> Cards { get; init; } = Enumerable.Empty<StorageLink>();

    public bool HasMoreCards => TotalCards > Cards.Sum(s => s.Copies);
}


public sealed record BinPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public IEnumerable<BoxPreview> Boxes { get; init; } = Enumerable.Empty<BoxPreview>();

    public bool Equals(BinPreview? bin)
    {
        return bin is not null
            && Id == bin.Id
            && Name == bin.Name
            && Boxes.SequenceEqual(bin.Boxes);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode()
            ^ Name.GetHashCode()
            ^ Boxes.Aggregate(0, (hash, c) => hash ^ c.GetHashCode());
    }
}

#endregion
