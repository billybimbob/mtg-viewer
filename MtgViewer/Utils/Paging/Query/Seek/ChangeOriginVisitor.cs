using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Seek;

internal sealed class ChangeOriginVisitor : ExpressionVisitor
{
    private readonly object? _origin;

    public ChangeOriginVisitor(object? newOrigin)
    {
        _origin = newOrigin;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsAfter(node))
        {
            var entityType = node.Method.GetGenericArguments()[0];

            return Expression.Call(
                instance: null,
                method: PagingExtensions.AfterReference
                    .MakeGenericMethod(entityType),
                arg0: node.Arguments[0],
                arg1: Expression.Constant(_origin, entityType));
        }

        return base.VisitMethodCall(node);
    }
}
