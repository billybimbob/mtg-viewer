using System;
using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class TranslateSeekVisitor : ExpressionVisitor
{
    private readonly IQueryProvider _provider;
    private readonly SeekFilter _seekFilter;
    private readonly SeekQueryExpression? _seekParameters;

    public TranslateSeekVisitor(IQueryProvider provider, EvaluateMemberVisitor evaluateMember)
    {
        _provider = provider;
        _seekFilter = new SeekFilter(evaluateMember);
        _seekParameters = null;
    }

    private TranslateSeekVisitor(TranslateSeekVisitor copy, SeekQueryExpression? seekParameters)
    {
        _provider = copy._provider;
        _seekFilter = copy._seekFilter;
        _seekParameters = seekParameters;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsSeekBy(node)
            && node.Arguments[1] is ConstantExpression { Value: SeekDirection direction })
        {
            var updatedSeek = UpdateSeek(direction, node.Type);
            var updatedTranslator = new TranslateSeekVisitor(this, updatedSeek);

            return updatedTranslator.ExpandSeek(node.Arguments[0]);
        }

        if (ExpressionHelpers.IsAfter(node)
            && node.Arguments[1] is ConstantExpression origin)
        {
            var updatedSeek = UpdateSeek(origin);
            var updatedTranslator = new TranslateSeekVisitor(this, updatedSeek);

            return updatedTranslator.Visit(node.Arguments[0]);
        }

        if (ExpressionHelpers.IsSeekTake(node)
            && node.Arguments[1] is ConstantExpression { Value: int size })
        {
            var updatedSeek = UpdateSeek(size, node.Type);
            var updatedTranslator = new TranslateSeekVisitor(this, updatedSeek);

            return updatedTranslator.Visit(node.Arguments[0]);
        }

        if (ExpressionHelpers.IsToSeekList(node))
        {
            return Visit(node.Arguments[0]);
        }

        return base.VisitMethodCall(node);
    }

    private Expression ExpandSeek(Expression source)
    {
        var direction = _seekParameters?.Direction;
        var origin = _seekParameters?.Origin;
        int? size = _seekParameters?.Size;

        var filter = _seekFilter.CreateFilter(source, direction, origin);
        var query = _provider.CreateQuery(source);

        var expandedQuery = (direction, filter, size) switch
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

        return expandedQuery.Expression;
    }

    private SeekQueryExpression UpdateSeek(SeekDirection direction, Type seekType)
    {
        var seekWithDirection = _seekParameters?.Update(direction);

        if (seekWithDirection is null)
        {
            var entityType = seekType.GenericTypeArguments[0];
            var origin = Expression.Constant(null, entityType);

            seekWithDirection = new SeekQueryExpression(origin, direction);
        }

        return seekWithDirection;
    }

    private SeekQueryExpression UpdateSeek(ConstantExpression origin)
        => _seekParameters?.Update(origin) ?? new SeekQueryExpression(origin);

    private SeekQueryExpression UpdateSeek(int size, Type seekType)
    {
        var seekWithSize = _seekParameters?.Update(size);

        if (seekWithSize is null)
        {
            var entityType = seekType.GenericTypeArguments[0];
            var origin = Expression.Constant(null, entityType);

            seekWithSize = new SeekQueryExpression(origin, size);
        }

        return seekWithSize;
    }
}
