namespace MtgViewer.Data.Projections;

public record LocationPreview
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    internal LocationType Type { get; init; }
}

public sealed record LocationCopy : LocationPreview
{
    public required int Copies { get; init; }
}
