using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Query.Filtering;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class RewriteSeekQueryVisitor : ExpressionVisitor
{
    private readonly IQueryProvider _provider;
    private readonly TranslateSeekVisitor _seekTranslator;
    private readonly SeekQueryExpression? _seek;

    public RewriteSeekQueryVisitor(IQueryProvider provider, SeekFilter seekFilter, SeekQueryExpression? seek = null)
    {
        _provider = provider;
        _seekTranslator = new TranslateSeekVisitor(provider, seekFilter, seek);
        _seek = seek;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsToSeekList(node))
        {
            return Visit(node.Arguments[0]);
        }

        if (node.Type.IsAssignableTo(typeof(IQueryable)))
        {
            return VisitQueryMethodCall(node);
        }

        return base.VisitMethodCall(node);
    }

    private Expression VisitQueryMethodCall(MethodCallExpression node)
    {
        var translatedNode = _seekTranslator.Visit(node);

        if (_seek?.Direction is SeekDirection.Backwards)
        {
            translatedNode = VisitBackwardsSeek(translatedNode);
        }

        return translatedNode;
    }

    private Expression VisitBackwardsSeek(Expression node)
    {
        if (node is MethodCallExpression call
            && ExpressionHelpers.IsReverse(call))
        {
            return node;
        }
        var reversedQuery = _provider
            .CreateQuery(node)
            .Reverse();

        return reversedQuery.Expression;
    }

    private sealed class TranslateSeekVisitor : ExpressionVisitor
    {
        private readonly IQueryProvider _provider;
        private readonly SeekFilter _seekFilter;
        private readonly SeekQueryExpression? _seek;

        public TranslateSeekVisitor(IQueryProvider provider, SeekFilter seekFilter, SeekQueryExpression? seek)
        {
            _provider = provider;
            _seekFilter = seekFilter;
            _seek = seek;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (ExpressionHelpers.IsSeekBy(node))
            {
                return VisitSeekBy(node);
            }

            if (ExpressionHelpers.IsAfter(node))
            {
                return Visit(node.Arguments[0]);
            }

            return base.VisitMethodCall(node);
        }

        private Expression VisitSeekBy(MethodCallExpression node)
        {
            var source = node.Arguments[0];

            var direction = _seek?.Direction;
            var origin = _seek?.Origin;

            var filter = _seekFilter.CreateFilter(source, direction, origin);
            var query = _provider.CreateQuery(source);

            if (direction is SeekDirection.Backwards)
            {
                query = query.Reverse();
            }

            if (filter is not null)
            {
                query = query.Where(filter);
            }

            return query.Expression;
        }
    }

}
