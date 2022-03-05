using System;
using System.Collections.Generic;
using System.Linq;
using MTGViewer.Data.Internal;

namespace MTGViewer.Data;


public record CardImage
{
    public string Id { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string ImageUrl { get; init; } = default!;
}


public record CardPreview
{
    public string Id { get; init; } = default!;
    public string Name { get; init; } = default!;

    public string? ManaCost { get; init; }
    public string SetName { get; init; } = default!;
    public Rarity Rarity { get; init; }

    public string ImageUrl { get; init; } = default!;
    public int Total { get; init; }
}


public record CardTotal(Card Card, int Total);


public class RecentTransaction
{
    public DateTime AppliedAt { get; init; }
    public IEnumerable<RecentChange> Changes { get; init; } = Enumerable.Empty<RecentChange>();
    public int Total { get; init; }
}


public record RecentChange
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


public record DeckPreview
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



public record DeckTradePreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public Color Color { get; init; }

    public bool SentTrades { get; init; }
    public bool ReceivedTrades { get; init; }
    public bool WantsCards { get; init; }
}


public record SuggestionPreview
{
    public int Id { get; init; }
    public DateTime SentAt { get; init; }

    public string CardId { get; init; } = default!;
    public string CardName { get; init; } = default!;
    public string? CardManaCost { get; init; }

    public string? ToName { get; init; }
    public string? Comment { get; init; }
}



public class BinPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public IEnumerable<BoxPreview> Boxes { get; init; } = Enumerable.Empty<BoxPreview>();
}


public class BoxPreview
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


public record BoxCard
{
    public string CardId { get; init; } = default!;
    public string CardName { get; init; } = default!;

    public string? CardManaCost { get; init; } = default!;
    public string CardSetName { get; init; } = default!;

    public int NumCopies { get; init; }
}



public record TransferPreview(
    TransactionPreview Transaction,
    LocationPreview To,
    LocationPreview? From,
    IReadOnlyList<ChangePreview> Changes);


public record TransactionPreview
{
    public int Id { get; init; }
    public DateTime AppliedAt { get; init; }
    public bool IsShared { get; init; }
}


public class ChangePreview
{
    public int Id { get; init; }
    public int Amount { get; init; }
    public TransactionPreview Transaction { get; init; } = default!;

    public LocationPreview To { get; init; } = default!;
    public LocationPreview? From { get; init; }

    public CardPreview Card { get; init; } = default!;
}


public record LocationPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    internal LocationType Type { get; init; }
}
