using System;
using System.Collections;
using System.Collections.Generic;

namespace EntityFrameworkCore.Paging;

public class OffsetList<T> : IReadOnlyList<T>
{
    private readonly IReadOnlyList<T> _items;

    public OffsetList(Offset offset, IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items = items;
        Offset = offset;
    }

    internal OffsetList() : this(new Offset(), Array.Empty<T>())
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
    public static OffsetList<T> Empty<T>() => Utils.EmptyOffsetList<T>.Value;
}
