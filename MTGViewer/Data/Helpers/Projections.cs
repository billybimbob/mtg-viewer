using System;
using System.Collections.Generic;
using System.Linq;
using MTGViewer.Data.Internal;

namespace MTGViewer.Data;


public class CardImage
{
    public string Id { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string ImageUrl { get; init; } = default!;
}


public class CardPreview
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


public class RecentChange
{
    public bool ToBox { get; init; }
    public bool FromBox { get; init; }
    public string CardName { get; init; } = default!;
}


public class TransactionPreview
{
    public DateTime AppliedAt { get; init; }
    public IEnumerable<RecentChange> Changes { get; init; } = Enumerable.Empty<RecentChange>();
    public int Total { get; init; }
}


public enum BuildState
{
    Theorycraft,
    Built,
    Requesting
}


public class DeckPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public Color Color { get; init; }
    public int CardTotal { get; init; }

    public bool HasWants { get; init; }
    public bool HasReturns { get; init; }
    public bool HasTradesTo { get; init; }

    public BuildState BuildState
    {
        get
        {
            if (HasTradesTo)
            {
                return BuildState.Requesting;
            }
            else if (HasWants || HasReturns)
            {
                return BuildState.Theorycraft;
            }
            else
            {
                return BuildState.Built;
            }
        }
    }
}



public class DeckTradePreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public Color Color { get; init; }

    public bool SentTrades { get; init; }
    public bool ReceivedTrades { get; init; }
    public bool WantsCards { get; init; }
}


public class SuggestionPreview
{
    public int Id { get; init; }
    public DateTime SentAt { get; init; }

    public string CardId { get; init; } = default!;
    public string CardName { get; init; } = default!;
    public string? CardManaCost { get; init; }

    public string? ToName { get; init; }
    public string? Comment { get; init; }
}




public record BinPreview(int Id, string Name, IEnumerable<BoxPreview> Boxes);



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


public class BoxCard
{
    public string CardId { get; init; } = default!;
    public string CardName { get; init; } = default!;

    public string? CardManaCost { get; init; } = default!;
    public string CardSetName { get; init; } = default!;

    public int NumCopies { get; init; }
}


// public class HistoryPreview
// {
//     public int Id { get; init; }
//     public DateTime AppliedAt { get; init; }
//     public bool IsShared { get; init; }
// }


// public record TransferPreview(
//     HistoryPreview Transaction,
//     LocationPreview To,
//     LocationPreview? From,
//     IReadOnlyList<ChangePreview> Changes);


// public class ChangePreview
// {
//     public int Id { get; init; }
//     public HistoryPreview Transaction { get; init; } = default!;

//     public LocationPreview? From { get; init; } = default!;
//     public LocationPreview To { get; init; } = default!;

//     public int Amount { get; init; }
//     public CardPreview Card { get; init; } = default!;

//     public bool IsShared => To.Type is LocationType.Box
//         && (From?.Type ?? LocationType.Box) is LocationType.Box;
// }


// public class LocationPreview
// {
//     public int Id { get; init; }
//     public string Name { get; init; } = default!;
//     internal LocationType Type { get; init; }
// }