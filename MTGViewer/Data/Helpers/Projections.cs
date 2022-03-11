using System;
using System.Collections.Generic;
using System.Linq;
using MTGViewer.Data.Internal;

namespace MTGViewer.Data;


public sealed record CardImage
{
    public string Id { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string ImageUrl { get; init; } = default!;
}


public sealed record CardPreview
{
    public string Id { get; init; } = default!;
    public string Name { get; init; } = default!;

    public string? ManaCost { get; init; }
    public string SetName { get; init; } = default!;
    public Rarity Rarity { get; init; }

    public string ImageUrl { get; init; } = default!;
    public int Total { get; init; }
}


public sealed record CardTotal(Card Card, int Total);


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



public sealed class BinPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public IEnumerable<BoxPreview> Boxes { get; init; } = Enumerable.Empty<BoxPreview>();
}


public sealed class BoxPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;

    public int BinId { get; init; }
    public string BinName { get; init; } = default!;

    public int Capacity { get; init; }
    public int TotalCards { get; init; }

    public IEnumerable<BoxCard> Cards { get; init; } = Enumerable.Empty<BoxCard>();

    public bool HasMoreCards => TotalCards > Cards.Sum(a => a.NumCopies);
}


public sealed record BoxCard
{
    public string CardId { get; init; } = default!;
    public string CardName { get; init; } = default!;

    public string? CardManaCost { get; init; } = default!;
    public string CardSetName { get; init; } = default!;

    public int NumCopies { get; init; }
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


public sealed class DeckColors
{
    public int Id { get; init; }
    public IEnumerable<Color> CardColors { get; init; } = Enumerable.Empty<Color>();
    public IEnumerable<Color> WantColors { get; init; } = Enumerable.Empty<Color>();
}