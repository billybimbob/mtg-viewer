using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class LinkedOrderProperty : IEnumerable<LinkedOrderProperty>
{
    private readonly LinkedOrderProperty? _previous;

    public LinkedOrderProperty(OrderProperty current, LinkedOrderProperty? previous)
    {
        _previous = previous;
        Member = current.Member;
        Ordering = current.Ordering;
        NullOrder = current.NullOrder;
    }

    public MemberExpression? Member { get; }

    public Ordering Ordering { get; }

    public NullOrder NullOrder { get; }

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
    {
        member = Member;
        ordering = Ordering;
        nullOrder = NullOrder;
    }
}
