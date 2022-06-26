namespace MtgViewer.Data.Projections;

public record LocationPreview
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    internal LocationType Type { get; init; }
}

public sealed record LocationCopy : LocationPreview
{
    public int Copies { get; init; }
}
