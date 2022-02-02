using System;
using System.Collections;
using System.Collections.Generic;

namespace MTGViewer.Data;

public readonly record struct Offset(int Current, int Total)
{
    private readonly int _current = Math.Max(Current, 0);
    private readonly int _total = Math.Max(Total, 0);


    public int Current
    {
        get => _current; 
        init => _current = Math.Max(value, 0);
    }

    public int Total
    {
        get => _total;
        init => _total = Math.Max(value, 0);
    }

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

        return (int) Math.Ceiling((double) totalItems / pageSize);
    }


    public override string ToString() => Current == Total 
        ? $"{Current}/{Total}" 
        : $"{Current + 1}/{Total}";
}



public class OffsetList<T> : IReadOnlyList<T>
{
    private static readonly OffsetList<T> _empty = new(default, Array.Empty<T>());

    private readonly IList<T> _items;

    public OffsetList(Offset offset, IList<T> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException("Items is null");
        }

        Offset = offset;
        _items = items;
    }

    public Offset Offset { get; }

    public int Count => _items.Count;

    public T this[int index] => _items[index];


    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    public static OffsetList<T> Empty() => _empty;
}