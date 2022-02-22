using System.Collections;
using System.Collections.Generic;

namespace System.Linq.Expressions;

internal class VisitedQuery<T> : IQueryable<T>
{
    private readonly IQueryable<T> _source;

    public VisitedQuery(IQueryable<T> source, ExpressionVisitor visitor)
    {
        var visited = visitor.Visit(source.Expression);

        if (!typeof(IQueryable<T>).IsAssignableFrom(visited.Type))
        {
            throw new ArgumentException(nameof(visited));
        }

        Expression = visited;

        _source = source;
    }

    public Expression Expression { get; }

    public Type ElementType => _source.ElementType;

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


internal static partial class QueryVisitExtensions
{
    /// <summary>
    /// Used to apply custom Expression visitors on an IQueryable. This method has a chance to
    /// create an invalid IQueryable based on result of the visitor
    /// </summary>
    internal static IQueryable<T> Visit<T>(this IQueryable<T> source, ExpressionVisitor visitor)
    {
        return new VisitedQuery<T>(source, visitor);
    }
}