using System.Diagnostics.CodeAnalysis;
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

internal sealed class AddSeekVisitor : ExpressionVisitor
{
    private readonly object? _origin;
    private readonly SeekDirection _direction;
    private readonly int? _take;

    private bool _foundSeek;

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

    [return: NotNullIfNotNull("node")]
    public override Expression? Visit(Expression? node)
    {
        _foundSeek = false;

        var visit = base.Visit(node);

        if (visit is null)
        {
            return null;
        }

        if (_foundSeek)
        {
            return visit;
        }

        return new SeekExpression(
            visit, Expression.Constant(_origin), _direction, _take);
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is not SeekExpression seek)
        {
            return node;
        }

        _foundSeek = true;

        return seek.Update(_origin, _direction, _take);
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

        var query = _provider.CreateQuery<TEntity>(node);

        var filter = OriginFilter.Build(query, _origin, seek.Direction);

        var filteredQuery = (seek.Direction, filter, seek.Take) switch
        {
            (SeekDirection.Forward, not null, int count) => query
                .Where(filter)
                .Take(count),

            (SeekDirection.Backwards, not null, int count) => query
                .Reverse()
                .Where(filter)
                .Take(count)
                .Reverse(),

            (SeekDirection.Forward, not null, null) => query
                .Where(filter),

            (SeekDirection.Backwards, not null, null) => query
                .Reverse()
                .Where(filter)
                .Reverse(),

            (SeekDirection.Forward, null, int count) => query
                .Take(count),

            (SeekDirection.Backwards, null, int count) => query
                .Reverse()
                .Take(count)
                .Reverse(),

            _ => query
        };

        return filteredQuery.Expression;
    }
}
