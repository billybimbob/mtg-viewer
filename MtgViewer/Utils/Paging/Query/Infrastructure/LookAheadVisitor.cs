using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class LookAheadVisitor : ExpressionVisitor
{
    private readonly ParseSeekTakeVisitor _seekTakeParser;
    private readonly IncrementCountVisitor _countIncrementor;

    public LookAheadVisitor(ParseSeekTakeVisitor seekTakeParser)
    {
        _seekTakeParser = seekTakeParser;
        _countIncrementor = new IncrementCountVisitor();
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (_seekTakeParser.IsTake(node))
        {
            var newArgs = _countIncrementor.Visit(node.Arguments);

            return node.Update(node.Object, newArgs);
        }

        return base.VisitMethodCall(node);
    }

    private sealed class IncrementCountVisitor : ExpressionVisitor
    {
        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is int count)
            {
                return Expression.Constant(count + 1);
            }

            return node;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
            => node;
    }
}
