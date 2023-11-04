using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Filtering;

internal sealed class LinkedOrderProperty : IEnumerable<LinkedOrderProperty>
{
    private readonly OrderProperty _property;
    private readonly LinkedOrderProperty? _previous;

    public LinkedOrderProperty(OrderProperty property, LinkedOrderProperty? previous)
    {
        _property = property;
        _previous = previous;
    }

    public MemberExpression? Member => _property.Member;
    public Ordering Ordering => _property.Ordering;
    public NullOrder NullOrder => _property.NullOrder;

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<LinkedOrderProperty> GetEnumerator()
    {
        yield return this;

        var current = _previous;

        while (current != null)
        {
            yield return current;
            current = current._previous;
        }
    }

    public void Deconstruct(out MemberExpression? member, out Ordering ordering, out NullOrder nullOrder)
        => _property.Deconstruct(out member, out ordering, out nullOrder);
}
