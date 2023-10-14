using System;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class SeekQueryExpression : Expression
{
    public SeekQueryExpression(SeekDirection direction, ConstantExpression origin, int? size = null)
    {
        Type = typeof(ISeekable<>).MakeGenericType(origin.Type);
        Origin = origin;
        Direction = direction;
        Size = size;
    }

    public SeekQueryExpression()
        : this(SeekDirection.Forward, Constant(null), null)
    {
    }

    public override ExpressionType NodeType => ExpressionType.Extension;

    public override Type Type { get; }

    public SeekDirection Direction { get; }

    public ConstantExpression Origin { get; }

    public int? Size { get; }

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var visitedOrigin = visitor.Visit(Origin);

        if (visitedOrigin is not ConstantExpression newOrigin)
        {
            throw new InvalidOperationException($"{nameof(Origin)} is invalid type: {visitedOrigin.Type.Name}");
        }

        return Update(Direction, newOrigin, Size);
    }

    public SeekQueryExpression Update(SeekDirection direction, ConstantExpression origin, int? size)
    {
        if (ExpressionEqualityComparer.Instance.Equals(origin, Origin)
            && direction == Direction
            && size == Size)
        {
            return this;
        }

        return new SeekQueryExpression(direction, origin, size);
    }

    public SeekQueryExpression Update(SeekDirection direction)
        => Update(direction, Origin, Size);

    public SeekQueryExpression Update(ConstantExpression origin)
        => Update(Direction, origin, Size);

    public SeekQueryExpression Update(int? size)
        => Update(Direction, Origin, size);
}
