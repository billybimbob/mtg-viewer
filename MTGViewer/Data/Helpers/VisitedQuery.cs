using System.Collections;
using System.Collections.Generic;

namespace System.Linq.Expressions;

internal class VisitedQuery<T> : IQueryable<T>
{
    private readonly IQueryable<T> _source;

    public VisitedQuery(IQueryable<T> source, Expression visited)
    {
        _source = source;
        Expression = visited;
    }

    public Type ElementType => _source.ElementType;

    public Expression Expression { get; }

    public IQueryProvider Provider => _source.Provider;


    public IEnumerator<T> GetEnumerator()
    {
        return _source.Provider
            .Execute<IEnumerable<T>>(Expression)
            .GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _source.Provider
            .Execute<IEnumerable>(Expression)
            .GetEnumerator();
    }
}


public static partial class QueryVisitExtensions
{
    public static IQueryable<T> Visit<T>(this IQueryable<T> source, ExpressionVisitor visitor)
    {
        var modifiedSource = visitor.Visit(source.Expression);

        return new VisitedQuery<T>(source, modifiedSource);
    }
}