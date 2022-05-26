namespace MtgViewer.Data.Treasury;

internal sealed record StorageSpace
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Held { get; set; }

    public int? Capacity { get; init; }

    public bool HasSpace => Held < Capacity || Capacity is null;

    public int? Remaining => Capacity - Held;
}
