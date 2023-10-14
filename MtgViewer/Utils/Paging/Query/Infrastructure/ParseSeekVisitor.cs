using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class ParseSeekVisitor : ExpressionVisitor
{
    private readonly FindSeekTakeVisitor _findSeekTake;

    public ParseSeekVisitor()
    {
        _findSeekTake = new FindSeekTakeVisitor();
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsSeekBy(node)
            && node.Arguments[1] is ConstantExpression { Value: SeekDirection direction })
        {
            var entityType = node.Method.GetGenericArguments()[0];

            return new SeekQueryExpression(direction, Expression.Constant(null, entityType));
        }

        if (Visit(node.Arguments.ElementAtOrDefault(0)) is not SeekQueryExpression seek)
        {
            return node;
        }

        if (ExpressionHelpers.IsAfter(node)
            && node.Arguments[1] is ConstantExpression origin)
        {
            return seek.Update(origin);
        }

        if (_findSeekTake.TryGetSeekTake(node, out int size))
        {
            return seek.Update(size);
        }

        return seek;
    }
}
