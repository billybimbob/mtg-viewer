namespace MtgViewer.Data.Projections;

public sealed record PlayerPreview
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int TotalDecks { get; init; }
}
