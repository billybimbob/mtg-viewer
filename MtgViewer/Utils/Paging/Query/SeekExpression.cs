using System;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class SeekExpression : Expression
{
    internal SeekExpression(ConstantExpression origin, SeekDirection direction, int? size)
    {
        Origin = origin;
        Direction = direction;
        Size = size;
    }

    public ConstantExpression Origin { get; }

    public SeekDirection Direction { get; }

    public int? Size { get; }

    public override Type Type => typeof(ISeekable<>).MakeGenericType(Origin.Type);

    public override ExpressionType NodeType => ExpressionType.Extension;

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var visitedOrigin = visitor.Visit(Origin);

        if (visitedOrigin is not ConstantExpression newOrigin)
        {
            throw new InvalidOperationException($"{nameof(Origin)} is invalid type: {visitedOrigin.Type.Name}");
        }

        return Update(newOrigin, Direction, Size);
    }

    public SeekExpression Update(ConstantExpression origin, SeekDirection direction, int? size)
    {
        if (ExpressionEqualityComparer.Instance.Equals(origin, Origin)
            && direction == Direction
            && size == Size)
        {
            return this;
        }

        return new SeekExpression(origin, direction, size);
    }
}
