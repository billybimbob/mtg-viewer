using System;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class SeekExpression : Expression
{
    internal SeekExpression(
        Expression query,
        ConstantExpression origin,
        SeekDirection direction,
        int? size)
    {
        if (!query.Type.IsAssignableTo(typeof(IOrderedQueryable))
            || query.Type.GenericTypeArguments.ElementAtOrDefault(0) is not Type entityType)
        {
            throw new ArgumentException(
                $"{query.Type.Name} is not a strongly typed {nameof(IOrderedQueryable)}", nameof(query));
        }

        if (origin.Value is not null && origin.Type != entityType)
        {
            throw new ArgumentException(
                $"{origin.Type.Name} is not expected type {entityType.Name}", nameof(origin));
        }

        Query = query;
        Origin = origin;

        Direction = direction;
        Size = size;

        Type = typeof(ISeekable<>).MakeGenericType(entityType);
    }

    public Expression Query { get; }

    public ConstantExpression Origin { get; }

    public SeekDirection Direction { get; }

    public int? Size { get; }

    public override Type Type { get; }

    public override ExpressionType NodeType => ExpressionType.Extension;

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var newQuery = visitor.Visit(Query);
        var newOrigin = visitor.Visit(Origin);

        if (newOrigin is not ConstantExpression o)
        {
            throw new InvalidOperationException($"{nameof(Origin)} is invalid type: {newOrigin.Type.Name}");
        }

        return Update(newQuery, o, Direction, Size);
    }

    public SeekExpression Update(
        Expression query,
        ConstantExpression origin,
        SeekDirection direction,
        int? size)
    {
        if (ExpressionEqualityComparer.Instance.Equals(query, Query)
            && ExpressionEqualityComparer.Instance.Equals(origin, Origin)
            && direction == Direction
            && size == Size)
        {
            return this;
        }

        return new SeekExpression(query, origin, direction, size);
    }
}
