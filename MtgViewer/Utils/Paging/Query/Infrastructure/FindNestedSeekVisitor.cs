using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class FindNestedSeekVisitor : ExpressionVisitor
{
    private readonly FindSeekMethodVisitor _findSeekMethod;

    public FindNestedSeekVisitor(ParseSeekTakeVisitor seekTakeParser)
    {
        _findSeekMethod = new FindSeekMethodVisitor(seekTakeParser);
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
            return _findSeekMethod.Visit(node);
        }

        var parent = node.Arguments.ElementAtOrDefault(0);
        var visitedParent = Visit(parent);

        if (visitedParent is not null && visitedParent != parent)
        {
            return visitedParent;
        }

        return node;
    }

    private sealed class FindSeekMethodVisitor : ExpressionVisitor
    {
        private readonly ParseSeekTakeVisitor _seekTakeParser;

        public FindSeekMethodVisitor(ParseSeekTakeVisitor seekTakeParser)
        {
            _seekTakeParser = seekTakeParser;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not MethodCallExpression parent)
            {
                return node;
            }

            if (ExpressionHelpers.IsSeekQuery(parent))
            {
                return parent;
            }

            if (_seekTakeParser.TryParse(parent, out _))
            {
                return parent;
            }

            if (Visit(parent) is Expression visitedParent
                && visitedParent != parent)
            {
                return visitedParent;
            }

            return node;
        }
    }
}
