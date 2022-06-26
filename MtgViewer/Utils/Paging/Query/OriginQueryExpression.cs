using System;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class OriginQueryExpression : Expression
{
    internal OriginQueryExpression(
        Expression source,
        ConstantExpression origin,
        MemberExpression? key)
    {
        if (!source.Type.IsAssignableTo(typeof(IQueryable))
            || source.Type.GenericTypeArguments.ElementAtOrDefault(0) is not Type entityType)
        {
            throw new ArgumentException(
                $"{source.Type.Name} is not a strongly typed {nameof(IQueryable)}", nameof(source));
        }

        if ((origin, key) is ({ Value: not null }, not null) && origin.Type != key.Type)
        {
            throw new ArgumentException($"{nameof(key)} does not have expected type of {origin.Type}");
        }

        Source = source;
        Origin = origin;
        Key = key;
        Type = entityType;
    }

    public Expression Source { get; }

    public ConstantExpression Origin { get; }

    public MemberExpression? Key { get; }

    public override Type Type { get; }

    public override ExpressionType NodeType => ExpressionType.Extension;

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var newSource = visitor.Visit(Source);
        var newOrigin = visitor.Visit(Origin);
        var newKey = visitor.Visit(Key);

        if (newOrigin is not ConstantExpression o)
        {
            throw new InvalidOperationException($"{nameof(Origin)} is invalid type: {newOrigin.Type.Name}");
        }

        if (newKey is not (MemberExpression or null))
        {
            throw new InvalidOperationException($"{nameof(Key)} is invalid type: {newKey.Type.Name}");
        }

        return Update(newSource, o, newKey as MemberExpression);
    }

    public OriginQueryExpression Update(
        Expression source,
        ConstantExpression origin,
        MemberExpression? key)
    {
        if (ExpressionEqualityComparer.Instance.Equals(source, Source)
            && ExpressionEqualityComparer.Instance.Equals(origin, Origin)
            && ExpressionEqualityComparer.Instance.Equals(key, Key))
        {
            return this;
        }

        return new OriginQueryExpression(source, origin, key);
    }
}
