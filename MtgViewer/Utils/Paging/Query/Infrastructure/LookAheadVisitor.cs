using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class LookAheadVisitor : ExpressionVisitor
{
    public static LookAheadVisitor Instance { get; } = new();

    private LookAheadVisitor()
    {
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsThenTake(node)
            && node.Arguments is [var parent, ConstantExpression { Value: int count }])
        {
            return node.Update(
                node.Object, new[] { parent, Expression.Constant(count + 1) });
        }

        return base.VisitMethodCall(node);
    }
}
