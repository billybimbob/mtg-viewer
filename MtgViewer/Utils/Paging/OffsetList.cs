using System;
using System.Collections;
using System.Collections.Generic;

namespace EntityFrameworkCore.Paging;

public class OffsetList<T> : IReadOnlyList<T>
{
    internal static OffsetList<T> Empty { get; } = new();

    private readonly IReadOnlyList<T> _items;

    public OffsetList(Offset offset, IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items = items;
        Offset = offset;
    }

    public OffsetList() : this(new Offset(), Array.Empty<T>())
    {
    }

    public Offset Offset { get; }

    public int Count => _items.Count;

    public T this[int index] => _items[index];

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
}

public static class OffsetList
{
    public static OffsetList<T> Empty<T>() => OffsetList<T>.Empty;
}
