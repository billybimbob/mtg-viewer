using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class FindNestedSeekVisitor : ExpressionVisitor
{
    private readonly FindOrderByVisitor _findOrderBy;

    public FindNestedSeekVisitor()
    {
        _findOrderBy = new FindOrderByVisitor();
    }

    public bool TryFind(Expression node, [NotNullWhen(true)] out Expression? nestedSeekQuery)
    {
        var visitedNode = Visit(node);

        if (visitedNode != node)
        {
            nestedSeekQuery = visitedNode;
            return true;
        }
        else
        {
            nestedSeekQuery = null;
            return false;
        }
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsSeekBy(node))
        {
            return _findOrderBy.Visit(node);
        }

        var parent = node.Arguments.ElementAtOrDefault(0);
        var visitedParent = Visit(parent);

        if (visitedParent is not null && visitedParent != parent)
        {
            return visitedParent;
        }

        return node;
    }

    private sealed class FindOrderByVisitor : ExpressionVisitor
    {
        private readonly FindSeekByVisitor _findSeekBy;

        public FindOrderByVisitor()
        {
            _findSeekBy = new FindSeekByVisitor();
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (ExpressionHelpers.IsOrderBy(node))
            {
                return VisitOrderBy(node);
            }

            var parent = node.Arguments.ElementAtOrDefault(0);
            var visitedParent = Visit(parent);

            if (visitedParent is not null && visitedParent != parent)
            {
                return visitedParent;
            }

            return node;
        }

        private Expression VisitOrderBy(MethodCallExpression orderByNode)
        {
            var seekBySearch = _findSeekBy.Visit(orderByNode);

            if (seekBySearch != orderByNode)
            {
                // want to get everything above the current order by
                return orderByNode.Arguments[0];
            }
            else
            {
                return orderByNode;
            }
        }
    }

    private sealed class FindSeekByVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not MethodCallExpression parent)
            {
                return node;
            }

            if (ExpressionHelpers.IsSeekBy(parent))
            {
                return parent;
            }

            var visitedParent = Visit(parent);

            if (visitedParent is not null && visitedParent != parent)
            {
                return visitedParent;
            }

            return node;
        }
    }
}
