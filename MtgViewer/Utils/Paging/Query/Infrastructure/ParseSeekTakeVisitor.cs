using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class ParseSeekTakeVisitor : ExpressionVisitor
{
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsTake(node)
            && node.Arguments.ElementAtOrDefault(0) is MethodCallExpression parent
            && ExpressionHelpers.IsSeekQuery(parent))
        {
            return node.Arguments[1];
        }

        return node;
    }

    public bool TryParse(Expression node, out int size)
    {
        if (Visit(node) is ConstantExpression { Value: int count })
        {
            size = count;
            return true;
        }
        else
        {
            size = 0;
            return false;
        }
    }
}
