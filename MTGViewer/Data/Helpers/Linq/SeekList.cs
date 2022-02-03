using System.Collections.Generic;

namespace System.Collections.Paging;

public readonly record struct Seek<T>(T? Previous, T? Next);


public class SeekList<T> : IReadOnlyList<T>
{
    private static readonly SeekList<T> _empty = new(default, Array.Empty<T>());

    private readonly IList<T> _items;

    public SeekList(Seek<T> seek, IList<T> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        Seek = seek;
        _items = items;
    }

    public Seek<T> Seek { get; }

    public int Count => _items.Count;

    public T this[int index] => _items[index];


    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    public static SeekList<T> Empty() => _empty;
}