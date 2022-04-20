namespace MTGViewer.Data.Treasury;

internal readonly record struct Assignment<TSource>(TSource Source, int Copies, Storage Target);

internal sealed record StorageSpace
{
    public int Id { get; init; }

    public string Name { get; init; } = default!;

    public int Held { get; set; }

    public int? Capacity { get; init; }

    public bool HasSpace => Held < Capacity || Capacity is null;

    public int? Remaining => Capacity - Held;
}
