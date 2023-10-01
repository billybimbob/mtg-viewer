using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class TranslateSeekVisitor : ExpressionVisitor
{
    private readonly FindSeekTakeVisitor _findSeekTake;
    private readonly IQueryProvider _provider;
    private readonly ConstantExpression? _origin;
    private readonly int? _size;

    public TranslateSeekVisitor(IQueryProvider provider)
    {
        _findSeekTake = new FindSeekTakeVisitor();
        _provider = provider;
        _origin = null;
        _size = null;
    }

    private TranslateSeekVisitor(FindSeekTakeVisitor findSeekTake, IQueryProvider provider, ConstantExpression? origin, int? size)
    {
        _findSeekTake = findSeekTake;
        _provider = provider;
        _origin = origin;
        _size = size;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsSeekBy(node)
            && node.Arguments[1] is ConstantExpression { Value: SeekDirection direction })
        {
            return ExpandToQuery(node.Arguments[0], direction).Expression;
        }

        if (ExpressionHelpers.IsAfter(node)
            && node.Arguments[1] is ConstantExpression origin)
        {
            var visitorWithOrigin = new TranslateSeekVisitor(_findSeekTake, _provider, origin, _size);

            return visitorWithOrigin.Visit(node.Arguments[0]);
        }

        if (_findSeekTake.TryGetSeekTake(node, out int size))
        {
            var visitorWithCount = new TranslateSeekVisitor(_findSeekTake, _provider, _origin, size);

            return visitorWithCount.Visit(node.Arguments[0]);
        }

        if (ExpressionHelpers.IsToSeekList(node))
        {
            return Visit(node.Arguments[0]);
        }

        return base.VisitMethodCall(node);
    }

    private IQueryable ExpandToQuery(Expression source, SeekDirection direction)
    {
        var seekFilter = new SeekFilter(source, _origin, direction);

        var filter = seekFilter.CreateFilter();

        var query = _provider.CreateQuery(source);

        return (direction, filter, _size) switch
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
    }
}
