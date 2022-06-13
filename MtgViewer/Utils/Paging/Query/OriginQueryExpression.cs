using System;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class OriginQueryExpression : Expression
{
    internal OriginQueryExpression(QueryRootExpression root, ConstantExpression origin, LambdaExpression? selector)
    {
        if (selector is not null
            && selector.Parameters.FirstOrDefault()?.Type != root.EntityType.ClrType)
        {
            throw new ArgumentException(
                $"{nameof(selector)} expected to have parameter type of {root.EntityType.ClrType.Name}", nameof(selector));
        }

        Root = root;
        Origin = origin;
        Selector = selector;
    }

    public QueryRootExpression Root { get; }

    public ConstantExpression Origin { get; }

    public LambdaExpression? Selector { get; }

    public override Type Type => Selector?.Body.Type ?? Root.EntityType.ClrType;

    public override ExpressionType NodeType => ExpressionType.Extension;

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var newRoot = visitor.Visit(Root);

        var newOrigin = visitor.Visit(Origin);

        var newSelector = visitor.Visit(Selector);

        if (newRoot is not QueryRootExpression r)
        {
            throw new InvalidOperationException($"{nameof(Root)} is invalid type: {newRoot.Type.Name}");
        }

        if (newOrigin is not ConstantExpression o)
        {
            throw new InvalidOperationException($"{nameof(Origin)} is invalid type: {newOrigin.Type.Name}");
        }

        if (newSelector is not (LambdaExpression or null))
        {
            throw new InvalidOperationException($"{nameof(Selector)} is invalid type: {newSelector.Type.Name}");
        }

        return Update(r, o, newSelector as LambdaExpression);
    }

    public OriginQueryExpression Update(
        QueryRootExpression root,
        ConstantExpression origin,
        LambdaExpression? selector)
    {
        if (ExpressionEqualityComparer.Instance.Equals(root, Root)
            && ExpressionEqualityComparer.Instance.Equals(origin, Origin)
            && ExpressionEqualityComparer.Instance.Equals(Selector, selector))
        {
            return this;
        }

        return new OriginQueryExpression(root, origin, selector);
    }
}
