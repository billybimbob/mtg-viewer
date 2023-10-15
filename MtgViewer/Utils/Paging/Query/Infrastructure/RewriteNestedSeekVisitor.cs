using System;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class RewriteNestedSeekVisitor : ExpressionVisitor
{
    private readonly ReplaceQueryVisitor _queryReplacer;

    public RewriteNestedSeekVisitor(Expression nestedSeekQuery)
    {
        _queryReplacer = new ReplaceQueryVisitor(nestedSeekQuery);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsOrderBy(node))
        {
            var newArgs = _queryReplacer.Visit(node.Arguments);

            return node.Update(node.Object, newArgs);
        }

        return base.VisitMethodCall(node);
    }

    private sealed class ReplaceQueryVisitor : ExpressionVisitor
    {
        private readonly Expression _newQuery;

        public ReplaceQueryVisitor(Expression newQuery)
        {
            if (!newQuery.Type.IsAssignableTo(typeof(IQueryable)))
            {
                throw new ArgumentException("Expression must be a query", nameof(newQuery));
            }

            _newQuery = newQuery;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Type == _newQuery.Type)
            {
                return _newQuery;
            }

            return node;
        }
    }
}
