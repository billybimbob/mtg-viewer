namespace EntityFrameworkCore.Paging.Utils;

internal static class EmptyOffsetList<T>
{
    internal static OffsetList<T> Value { get; } = new();
}

internal static class EmptySeekList<T> where T : class
{
    internal static SeekList<T> Value { get; } = new();
}
