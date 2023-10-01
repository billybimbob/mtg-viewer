using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class LookAheadVisitor : ExpressionVisitor
{
    private readonly FindSeekTakeVisitor _findSeekTake;

    public LookAheadVisitor()
    {
        _findSeekTake = new FindSeekTakeVisitor();
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (_findSeekTake.TryGetSeekTake(node, out int size))
        {
            return node.Update(
                node.Object,
                node.Arguments
                    .SkipLast(1)
                    .Append(Expression.Constant(size + 1)));
        }

        return base.VisitMethodCall(node);
    }
}
