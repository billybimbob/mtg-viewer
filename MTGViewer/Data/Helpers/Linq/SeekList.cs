using System.Linq;
using System.Collections.Generic;

namespace System.Collections.Paging;

public readonly record struct Seek<TEntity>(TEntity? Previous, TEntity? Next)
    where TEntity : class
{
    public Seek(bool takePrevious, bool takeNext, IReadOnlyList<TEntity> items) : this(
        takePrevious ? items.ElementAtOrDefault(0) : null,
        takeNext ? items.ElementAtOrDefault(^1) : null)
    { }
}


public class SeekList<TEntity> : IReadOnlyList<TEntity> where TEntity : class
{
    private static readonly SeekList<TEntity> _empty = new(default, Array.Empty<TEntity>());

    private readonly IList<TEntity> _items;

    public SeekList(Seek<TEntity> seek, IList<TEntity> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        Seek = seek;
        _items = items;
    }

    public Seek<TEntity> Seek { get; }

    public int Count => _items.Count;

    public TEntity this[int index] => _items[index];


    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<TEntity> GetEnumerator() => _items.GetEnumerator();

    public static SeekList<TEntity> Empty() => _empty;
}