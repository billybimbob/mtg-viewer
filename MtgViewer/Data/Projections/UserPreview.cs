namespace MtgViewer.Data.Projections;

public sealed record UserPreview
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}
