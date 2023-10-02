using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal class FindOrderingVisitor : ExpressionVisitor
{
    private readonly FindSeekByVisitor _findSeekBy;

    public FindOrderingVisitor()
    {
        _findSeekBy = new FindSeekByVisitor();
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsOrderBy(node))
        {
            return KeepSeekingOrderBy(node);
        }

        return base.VisitMethodCall(node);
    }

    private Expression KeepSeekingOrderBy(MethodCallExpression node)
    {
        var parent = _findSeekBy.Visit(node.Arguments[0]);

        if (_findSeekBy.CallsSeekBy(parent))
        {
            return node.Arguments[0];
        }

        return node;
    }

    private sealed class FindSeekByVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (ExpressionHelpers.IsSeekBy(node))
            {
                return node;
            }

            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
            {
                return node;
            }

            return Visit(parent);
        }

        public bool CallsSeekBy(Expression node)
        {
            var visited = Visit(node);
            return ExpressionHelpers.IsSeekBy(visited);
        }
    }
}
