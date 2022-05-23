namespace MTGViewer.Data.Projections;

public sealed record LocationPreview
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    internal LocationType Type { get; init; }
}

