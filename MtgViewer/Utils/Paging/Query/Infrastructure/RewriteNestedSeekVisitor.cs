using System;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class RewriteNestedSeekVisitor : ExpressionVisitor
{
    private readonly Expression _nestedSeekQuery;

    public RewriteNestedSeekVisitor(Expression nestedSeekQuery)
    {
        if (!nestedSeekQuery.Type.IsAssignableTo(typeof(IQueryable)))
        {
            throw new ArgumentException("Expression must be a query", nameof(nestedSeekQuery));
        }

        _nestedSeekQuery = nestedSeekQuery;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsOrderBy(node))
        {
            return node.Update(
                node.Object,
                node.Arguments
                    .Skip(1)
                    .Prepend(_nestedSeekQuery));
        }

        return base.VisitMethodCall(node);
    }
}
