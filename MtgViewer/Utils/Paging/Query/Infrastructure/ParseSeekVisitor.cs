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

            return new SeekExpression(Expression.Constant(null, entityType), direction, size: null);
        }

        if (Visit(node.Arguments.ElementAtOrDefault(0)) is not Expression parent)
        {
            return node;
        }

        if (parent is not SeekExpression seek)
        {
            return parent;
        }

        if (ExpressionHelpers.IsAfter(node)
            && node.Arguments[1] is ConstantExpression origin)
        {
            return seek.Update(origin, seek.Direction, seek.Size);
        }

        if (_findSeekTake.TryGetSeekTake(node, out int size))
        {
            return seek.Update(seek.Origin, seek.Direction, size);
        }

        return seek;
    }
}
