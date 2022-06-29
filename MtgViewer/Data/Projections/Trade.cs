namespace MtgViewer.Data.Projections;

public sealed record TradePreview
{
    public int Id { get; init; }

    public CardPreview Card { get; init; } = default!;

    public DeckDetails Target { get; init; } = default!;

    public int Copies { get; init; }
}

public sealed record TradeDeckPreview
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public Color Color { get; init; }

    public bool SentTrades { get; init; }

    public bool ReceivedTrades { get; init; }

    public bool WantsCards { get; init; }
}
