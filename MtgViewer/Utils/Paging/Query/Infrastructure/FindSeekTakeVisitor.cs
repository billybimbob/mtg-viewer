using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class FindSeekTakeVisitor : ExpressionVisitor
{
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsSeekQuery(node))
        {
            return node;
        }

        if (Visit(node.Arguments.ElementAtOrDefault(0)) is not Expression parent)
        {
            return node;
        }

        if (ExpressionHelpers.IsTake(node) && ExpressionHelpers.IsSeekQuery(parent))
        {
            return node;
        }

        return parent;
    }

    public bool TryGetSeekTake(Expression node, out int size)
    {
        size = 0;

        if (Visit(node) is MethodCallExpression call
            && call == node
            && call.Arguments[1] is ConstantExpression { Value: int count })
        {
            size = count;
            return true;
        }

        return false;
    }
}
