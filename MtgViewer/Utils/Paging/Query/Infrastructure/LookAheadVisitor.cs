using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class LookAheadVisitor : ExpressionVisitor
{
    private readonly ParseSeekTakeVisitor _seekTakeParser;

    public LookAheadVisitor(ParseSeekTakeVisitor seekTakeParser)
    {
        _seekTakeParser = seekTakeParser;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (_seekTakeParser.TryParse(node, out int size))
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
