using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Seek;

internal sealed class TranslateSeekVisitor : ExpressionVisitor
{
    private readonly IQueryProvider _provider;

    private SeekDirection? _direction;
    private ConstantExpression? _origin;
    private int? _size;

    public TranslateSeekVisitor(IQueryProvider provider)
    {
        _provider = provider;
    }

    [return: NotNullIfNotNull("node")]
    public override Expression? Visit(Expression? node)
    {
        _direction = null;
        _origin = null;
        _size = null;

        return base.Visit(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsSeekBy(node)
            && node.Arguments[1] is ConstantExpression { Value: SeekDirection direction })
        {
            _direction = direction;

            return ExpandToQuery(node.Arguments[0]).Expression;
        }

        if (ExpressionHelpers.IsAfter(node)
            && node.Arguments[1] is ConstantExpression origin)
        {
            _origin = origin;

            return base.Visit(node.Arguments[0]);
        }

        if (ExpressionHelpers.IsThenTake(node)
            && node.Arguments[1] is ConstantExpression { Value: int count })
        {
            _size = count;

            return base.Visit(node.Arguments[0]);
        }

        if (ExpressionHelpers.IsToSeekList(node))
        {
            return base.Visit(node.Arguments[0]);
        }

        return base.VisitMethodCall(node);
    }

    private IQueryable ExpandToQuery(Expression source)
    {
        var query = _provider.CreateQuery(source);

        var filter = SeekFilter.Build(source, _origin, _direction);

        return (_direction, filter, _size) switch
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
