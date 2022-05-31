namespace MtgViewer.Data.Projections;

public sealed record QuantityLocationPreview
{
    public LocationPreview Location { get; init; } = default!;

    public int Copies { get; init; }
}

public sealed record QuantityCardPreview
{
    public int Id { get; init; }

    public CardPreview Card { get; init; } = default!;

    public int Copies { get; init; }
}

