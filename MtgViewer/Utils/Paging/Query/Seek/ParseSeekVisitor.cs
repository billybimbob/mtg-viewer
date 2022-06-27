using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Seek;

internal sealed class ParseSeekVisitor : ExpressionVisitor
{
    private static ParseSeekVisitor? _instance;
    public static ParseSeekVisitor Instance => _instance ??= new();

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

        if (ExpressionHelpers.IsThenTake(node)
            && node.Arguments[1] is ConstantExpression { Value: int count })
        {
            return seek.Update(seek.Origin, seek.Direction, count);
        }

        return parent;
    }
}
