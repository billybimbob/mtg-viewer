using System;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class ChangeOriginVisitor : ExpressionVisitor
{
    private readonly object? _origin;

    public ChangeOriginVisitor(object? newOrigin)
    {
        _origin = newOrigin;
    }

    public static Expression Visit(Expression expression, object? newOrigin)
    {
        var changeOrigin = new ChangeOriginVisitor(newOrigin);

        return changeOrigin.Visit(expression);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (!ExpressionHelpers.IsAfter(node))
        {
            return base.VisitMethodCall(node);
        }

        if (node.Method.GetGenericArguments() is not [Type entityType]
            || node.Arguments is not [Expression source])
        {
            return base.VisitMethodCall(node);
        }

        return Expression.Call(
            instance: null,
            method: PagingExtensions.AfterReference
                .MakeGenericMethod(entityType),
            arg0: source,
            arg1: Expression.Constant(_origin, entityType));

    }
}
