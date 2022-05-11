using System;

namespace MTGViewer.Data.Projections;

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
