using System.Linq;
using System.Linq.Expressions;

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

internal sealed class RemoveSeekVisitor : ExpressionVisitor
{
    private static RemoveSeekVisitor? _instance;
    public static RemoveSeekVisitor Instance => _instance ??= new();

    protected override Expression VisitExtension(Expression node)
    {
        if (node is SeekExpression seek)
        {
            return seek.Query;
        }

        return node;
    }
}

internal sealed class ExpandSeekVisitor<TEntity> : ExpressionVisitor
{
    private readonly IQueryProvider _provider;
    private readonly TEntity? _origin;

    public ExpandSeekVisitor(IQueryProvider provider, TEntity? origin)
    {
        _provider = provider;
        _origin = origin;
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is not SeekExpression seek)
        {
            return node;
        }

        var query = _provider.CreateQuery<TEntity>(seek.Query);

        var filter = OriginFilter.Build(query, _origin, seek.Direction);

        var filteredQuery = (seek.Direction, filter, seek.Size) switch
        {
            (SeekDirection.Forward, not null, int size) => query
                .Where(filter)
                .Take(size),

            (SeekDirection.Backwards, not null, int size) => query
                .Reverse()
                .Where(filter)
                .Take(size)
                .Reverse(),

            (SeekDirection.Forward, not null, null) => query
                .Where(filter),

            (SeekDirection.Backwards, not null, null) => query
                .Reverse()
                .Where(filter)
                .Reverse(),

            (SeekDirection.Forward, null, int size) => query
                .Take(size),

            (SeekDirection.Backwards, null, int size) => query
                .Reverse()
                .Take(size)
                .Reverse(),

            _ => query
        };

        return filteredQuery.Expression;
    }
}
