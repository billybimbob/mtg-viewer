using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class ParseSeekVisitor : ExpressionVisitor
{
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsSeekBy(node)
            && node.Arguments[1] is ConstantExpression { Value: SeekDirection direction })
        {
            var entityType = node.Type.GenericTypeArguments[0];
            var nullOrigin = Expression.Constant(null, entityType);

            return new SeekQueryExpression(nullOrigin, direction);
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

        if (ExpressionHelpers.IsSeekTake(node)
            && node.Arguments[1] is ConstantExpression { Value: int size }
            && (seek.Size is null || seek.Size > size))
        {
            return seek.Update(size);
        }

        return seek;
    }

    public SeekQueryExpression? Parse(Expression node)
        => Visit(node) as SeekQueryExpression;
}
