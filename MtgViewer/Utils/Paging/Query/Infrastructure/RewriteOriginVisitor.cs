using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class RewriteOriginVisitor : ExpressionVisitor
{
    private readonly object? _origin;

    public RewriteOriginVisitor(object? newOrigin)
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
                method: PagingMethods.AfterReference
                    .MakeGenericMethod(entityType),
                arg0: node.Arguments[0],
                arg1: Expression.Constant(_origin, entityType));
        }

        return base.VisitMethodCall(node);
    }
}
