using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Query.Infrastructure.Filter;
using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

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

    [return: NotNullIfNotNull(nameof(node))]
    public override Expression? Visit(Expression? node)
    {
        _direction = null;
        _origin = null;
        _size = null;

        return base.Visit(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments is not [Expression parent, ..])
        {
            return base.VisitMethodCall(node);
        }

        if (ExpressionHelpers.IsSeekBy(node)
            && node.Arguments is [_, ConstantExpression { Value: SeekDirection direction }])
        {
            _direction = direction;

            return ExpandToQuery(parent).Expression;
        }

        if (ExpressionHelpers.IsAfter(node)
            && node.Arguments is [_, ConstantExpression origin])
        {
            _origin = origin;

            return base.Visit(parent);
        }

        if (ExpressionHelpers.IsThenTake(node)
            && node.Arguments is [_, ConstantExpression { Value: int count }])
        {
            _size = count;

            return base.Visit(parent);
        }

        if (ExpressionHelpers.IsToSeekList(node))
        {
            return base.Visit(parent);
        }

        return base.VisitMethodCall(node);
    }

    private IQueryable ExpandToQuery(Expression source)
    {
        var filter = SeekFilter.Build(source, _origin, _direction);

        var query = _provider.CreateQuery(source);

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
