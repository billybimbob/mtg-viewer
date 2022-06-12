using System;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal class SeekExpression : Expression
{
    internal SeekExpression(Expression query, ConstantExpression origin, SeekDirection direction, int? take)
    {
        if (query.Type.IsGenericType is false)
        {
            throw new ArgumentException($"{query.Type.Name} is not {nameof(IQueryable)}", nameof(query));
        }

        var elementType = query.Type.GenericTypeArguments[0];

        if (!query.Type.IsAssignableTo(typeof(IQueryable<>).MakeGenericType(elementType)))
        {
            throw new ArgumentException($"{query.Type.Name} is not {nameof(IQueryable)}", nameof(query));
        }

        if (elementType.IsValueType)
        {
            throw new ArgumentException("Inner type is not a reference type", nameof(query));
        }

        Type = typeof(ISeekQueryable<>).MakeGenericType(elementType);
        Query = query;

        Origin = origin;
        Direction = direction;
        Take = take;
    }

    public override Type Type { get; }

    public Expression Query { get; }

    public ConstantExpression Origin { get; }

    public SeekDirection Direction { get; }

    public int? Take { get; }

    public override ExpressionType NodeType => ExpressionType.Extension;

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var newQuery = visitor.Visit(Query);
        var newOrigin = visitor.Visit(Origin);

        bool hasChanges = newQuery != Query || newOrigin != Origin;

        return (newOrigin, hasChanges) switch
        {
            (ConstantExpression o, true) => new SeekExpression(newQuery, o, Direction, Take),
            (_, true) => new SeekExpression(newQuery, Origin, Direction, Take),
            _ => this
        };
    }

    public SeekExpression Update(
        Expression query,
        ConstantExpression origin,
        SeekDirection direction,
        int? take)
    {
        if (ExpressionEqualityComparer.Instance.Equals(query, Query)
            && ExpressionEqualityComparer.Instance.Equals(origin, Origin)
            && direction == Direction
            && take == Take)
        {
            return this;
        }

        return new SeekExpression(query, origin, direction, take);
    }
}
