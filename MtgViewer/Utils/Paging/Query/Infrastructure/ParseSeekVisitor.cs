using System;
using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class ParseSeekVisitor : ExpressionVisitor
{
    public static ParseSeekVisitor Instance { get; } = new();

    private ParseSeekVisitor()
    {
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsSeekBy(node)
            && node.Arguments is [_, ConstantExpression { Value: SeekDirection direction }]
            && node.Method.GetGenericArguments() is [Type entityType])
        {
            return new SeekExpression(Expression.Constant(null, entityType), direction, size: null);
        }

        if (Visit(node.Arguments.ElementAtOrDefault(0)) is not Expression parent)
        {
            return node;
        }

        if (parent is not SeekExpression seek)
        {
            return parent;
        }

        if (ExpressionHelpers.IsAfter(node)
            && node.Arguments is [_, ConstantExpression origin])
        {
            return seek.Update(origin, seek.Direction, seek.Size);
        }

        if (ExpressionHelpers.IsThenTake(node)
            && node.Arguments is [_, ConstantExpression { Value: int count }])
        {
            return seek.Update(seek.Origin, seek.Direction, count);
        }

        return seek;
    }
}
