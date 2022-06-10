using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class FindSeekVisitor : ExpressionVisitor
{
    private static FindSeekVisitor? _instance;
    public static FindSeekVisitor Instance => _instance ??= new();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        if (parent is SeekExpression seek)
        {
            return seek;
        }

        return Visit(parent);
    }
}

internal sealed class AddSeekVisitor : ExpressionVisitor
{
    private readonly object? _origin;
    private readonly SeekDirection _direction;
    private readonly int? _take;

    public AddSeekVisitor(object? origin, SeekDirection direction, int? take)
    {
        _origin = origin;
        _direction = direction;
        _take = take;
    }

    public AddSeekVisitor(SeekExpression seek)
        : this(seek.Origin.Value, seek.Direction, seek.Take)
    {
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is SeekExpression seek)
        {
            return seek.Update(_origin, _direction, _take);
        }

        if (node is QueryRootExpression root)
        {
            return new SeekExpression(root, Expression.Constant(_origin), _direction, _take);
        }

        return node;
    }
}

internal sealed class RemoveSeekVisitor : ExpressionVisitor
{
    private static RemoveSeekVisitor? _instance;
    public static RemoveSeekVisitor Instance => _instance ??= new();

    protected override Expression VisitExtension(Expression node)
    {
        if (node is SeekExpression seek)
        {
            return seek.Root;
        }

        return node;
    }
}
