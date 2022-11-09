namespace MtgViewer.Data.Projections;

public sealed record TradePreview
{
    public required int Id { get; init; }

    public required CardPreview Card { get; init; }

    public required DeckDetails Target { get; init; }

    public required int Copies { get; init; }
}

public sealed record TradeDeckPreview
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required Color Color { get; init; }

    public required bool SentTrades { get; init; }

    public required bool ReceivedTrades { get; init; }

    public required bool WantsCards { get; init; }
}
