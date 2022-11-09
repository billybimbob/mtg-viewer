namespace MtgViewer.Data.Projections;

public sealed record PlayerPreview
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public int TotalDecks { get; init; }
}
