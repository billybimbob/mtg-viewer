using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class LookAheadVisitor : ExpressionVisitor
{
    public static LookAheadVisitor Instance { get; } = new();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsThenTake(node)
            && node.Arguments[1] is ConstantExpression { Value: int count })
        {
            return node.Update(
                node.Object,
                node.Arguments
                    .SkipLast(1)
                    .Append(Expression.Constant(count + 1)));
        }

        return base.VisitMethodCall(node);
    }
}
