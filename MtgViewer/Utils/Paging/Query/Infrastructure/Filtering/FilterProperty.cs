using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class FilterProperty : IEnumerable<FilterProperty>
{
    private readonly FilterProperty? _previous;

    public FilterProperty(KeyOrder current, FilterProperty? previous)
    {
        var (parameter, ordering) = current;
        _previous = previous;

        Parameter = parameter;
        Ordering = ordering;
    }

    public MemberExpression? Parameter { get; }

    public Ordering Ordering { get; }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<FilterProperty> GetEnumerator()
    {
        yield return this;

        var current = _previous;

        while (current != null)
        {
            yield return current;
            current = current._previous;
        }
    }
}
