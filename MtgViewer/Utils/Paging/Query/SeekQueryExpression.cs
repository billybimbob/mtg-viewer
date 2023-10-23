using System;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class SeekQueryExpression : Expression, IEquatable<Expression>
{
    public SeekQueryExpression(ConstantExpression origin, SeekDirection direction = SeekDirection.Forward, int? size = null)
    {
        ArgumentNullException.ThrowIfNull(origin);

        Type = typeof(ISeekable<>).MakeGenericType(origin.Type);
        Origin = origin;
        Direction = direction;
        Size = size;
    }

    public SeekQueryExpression(Type originType, SeekDirection direction = SeekDirection.Forward, int? size = null)
    {
        ArgumentNullException.ThrowIfNull(originType);

        if (!originType.IsClass)
        {
            throw new ArgumentException("Origin type must be a class type", nameof(originType));
        }

        Type = typeof(ISeekable<>).MakeGenericType(originType);
        Origin = Constant(null, originType);
        Direction = direction;
        Size = size;
    }

    public override ExpressionType NodeType => ExpressionType.Extension;

    public override Type Type { get; }

    public ConstantExpression Origin { get; }

    public SeekDirection Direction { get; }

    public int? Size { get; }

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        if (visitor.Visit(Origin) is not ConstantExpression visitedOrigin)
        {
            throw new InvalidOperationException($"{nameof(Origin)} is invalid type");
        }

        return Update(visitedOrigin, Direction, Size);
    }

    public SeekQueryExpression Update(ConstantExpression origin, SeekDirection direction, int? size)
    {
        if (direction == Direction
            && size == Size
            && ExpressionEqualityComparer.Instance.Equals(origin, Origin))
        {
            return this;
        }

        return new SeekQueryExpression(origin, direction, size);
    }

    public SeekQueryExpression Update(SeekDirection direction)
        => Update(Origin, direction, Size);

    public SeekQueryExpression Update(ConstantExpression origin)
        => Update(origin, Direction, Size);

    public SeekQueryExpression Update(int? size)
        => Update(Origin, Direction, size);

    public bool Equals(Expression? other)
        => other is SeekQueryExpression otherSeek
            && otherSeek.Direction == Direction
            && otherSeek.Size == Size
            && ExpressionEqualityComparer.Instance.Equals(otherSeek.Origin, Origin);
}
