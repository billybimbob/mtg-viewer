using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class TranslateSeekVisitor : ExpressionVisitor
{
    private readonly IQueryProvider _provider;
    private readonly ParseSeekTakeVisitor _seekTakeParser;
    private readonly SeekFilter _seekFilter;
    private readonly SeekQueryExpression _seekParameters;

    public TranslateSeekVisitor(IQueryProvider provider, ParseSeekTakeVisitor seekTakeParser, EvaluateMemberVisitor evaluateMember)
    {
        _provider = provider;
        _seekTakeParser = seekTakeParser;
        _seekFilter = new SeekFilter(evaluateMember);
        _seekParameters = new SeekQueryExpression();
    }

    private TranslateSeekVisitor(TranslateSeekVisitor copy, SeekQueryExpression seekParameters)
    {
        _provider = copy._provider;
        _seekTakeParser = copy._seekTakeParser;
        _seekFilter = copy._seekFilter;
        _seekParameters = seekParameters;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsSeekBy(node)
            && node.Arguments[1] is ConstantExpression { Value: SeekDirection direction })
        {
            var visitorWithDirection = new TranslateSeekVisitor(this, _seekParameters.Update(direction));

            return visitorWithDirection.ExpandToQuery(node.Arguments[0]).Expression;
        }

        if (ExpressionHelpers.IsAfter(node)
            && node.Arguments[1] is ConstantExpression origin)
        {
            var visitorWithOrigin = new TranslateSeekVisitor(this, _seekParameters.Update(origin));

            return visitorWithOrigin.Visit(node.Arguments[0]);
        }

        if (_seekTakeParser.TryParse(node, out int size))
        {
            var visitorWithSize = new TranslateSeekVisitor(this, _seekParameters.Update(size));

            return visitorWithSize.Visit(node.Arguments[0]);
        }

        if (ExpressionHelpers.IsToSeekList(node))
        {
            return Visit(node.Arguments[0]);
        }

        return base.VisitMethodCall(node);
    }

    private IQueryable ExpandToQuery(Expression source)
    {
        var direction = _seekParameters.Direction;
        var origin = _seekParameters.Origin;
        int? size = _seekParameters.Size;

        var filter = _seekFilter.CreateFilter(source, direction, origin);
        var query = _provider.CreateQuery(source);

        return (direction, filter, size) switch
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
    }
}
