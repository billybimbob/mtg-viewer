using System;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class OriginQueryExpression : Expression
{
    internal OriginQueryExpression(
        QueryRootExpression root,
        ConstantExpression origin,
        LambdaExpression? key,
        LambdaExpression? selector)
    {
        Root = root;
        Origin = origin;
        Key = key;
        Selector = selector;
    }

    public QueryRootExpression Root { get; }

    public ConstantExpression Origin { get; }

    public LambdaExpression? Key { get; }

    public LambdaExpression? Selector { get; }

    public override Type Type
        => Selector?.Body.Type ?? Root.EntityType.ClrType;

    public override ExpressionType NodeType => ExpressionType.Extension;

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var newRoot = visitor.Visit(Root);
        var newOrigin = visitor.Visit(Origin);
        var newKey = visitor.Visit(Key);
        var newSelector = visitor.Visit(Selector);

        if (newRoot is not QueryRootExpression r)
        {
            throw new InvalidOperationException($"{nameof(Root)} is invalid type: {newRoot.Type.Name}");
        }

        if (newOrigin is not ConstantExpression o)
        {
            throw new InvalidOperationException($"{nameof(Origin)} is invalid type: {newOrigin.Type.Name}");
        }

        if (newKey is not (LambdaExpression or null))
        {
            throw new InvalidOperationException($"{nameof(Key)} is invalid type: {newKey.Type.Name}");
        }

        if (newSelector is not (LambdaExpression or null))
        {
            throw new InvalidOperationException($"{nameof(Selector)} is invalid type: {newSelector.Type.Name}");
        }

        return Update(r, o, newKey as LambdaExpression, newSelector as LambdaExpression);
    }

    public OriginQueryExpression Update(
        QueryRootExpression root,
        ConstantExpression origin,
        LambdaExpression? key,
        LambdaExpression? selector)
    {
        if (ExpressionEqualityComparer.Instance.Equals(root, Root)
            && ExpressionEqualityComparer.Instance.Equals(origin, Origin)
            && ExpressionEqualityComparer.Instance.Equals(key, Key)
            && ExpressionEqualityComparer.Instance.Equals(Selector, selector))
        {
            return this;
        }

        return new OriginQueryExpression(root, origin, key, selector);
    }
}
