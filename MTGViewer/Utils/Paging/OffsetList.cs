using System.Collections;
using System.Collections.Generic;

namespace System.Paging;

public readonly record struct Offset(int Current, int Total)
{
    public int Current { get; init; } = Math.Max(Current, 0);
    public int Total { get; init; } = Math.Max(Total, 0);

    public bool HasPrevious => Current > 0;
    public bool HasNext => Current < Total - 1;
    public bool HasMultiple => Total > 1;

    public Offset(int currentPage, int totalItems, int pageSize)
        : this(currentPage, TotalPages(totalItems, pageSize))
    { }

    private static int TotalPages(int totalItems, int pageSize)
    {
        totalItems = Math.Max(totalItems, 0);
        pageSize = Math.Max(pageSize, 1);

        return (int)Math.Ceiling((double)totalItems / pageSize);
    }

    public override string ToString() => Current == Total
        ? $"{Current}/{Total}"
        : $"{Current + 1}/{Total}";
}

public class OffsetList<T> : IReadOnlyList<T>
{
    private readonly IReadOnlyList<T> _items;

    public OffsetList(Offset offset, IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Offset = offset;
        _items = items;
    }

    public Offset Offset { get; }

    public int Count => _items.Count;

    public T this[int index] => _items[index];

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    private static OffsetList<T>? s_empty;
    public static OffsetList<T> Empty => s_empty ??= new(default, Array.Empty<T>());
}
