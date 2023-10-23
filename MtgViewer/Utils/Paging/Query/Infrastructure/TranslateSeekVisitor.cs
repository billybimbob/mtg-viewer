using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class TranslateSeekVisitor : ExpressionVisitor
{
    private readonly IQueryProvider _provider;
    private readonly RewriteSeekQueryVisitor _seekQueryRewriter;
    private readonly SeekQueryExpression? _seek;

    public TranslateSeekVisitor(IQueryProvider provider, SeekFilter seekFilter, SeekQueryExpression? seek = null)
    {
        _provider = provider;
        _seekQueryRewriter = new RewriteSeekQueryVisitor(provider, seekFilter, seek);
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
        var rewrittenNode = _seekQueryRewriter.Visit(node);

        if (_seek?.Direction is SeekDirection.Backwards)
        {
            var reversedQuery = _provider
                .CreateQuery(rewrittenNode)
                .Reverse();

            rewrittenNode = reversedQuery.Expression;
        }

        return rewrittenNode;
    }

    private sealed class RewriteSeekQueryVisitor : ExpressionVisitor
    {
        private readonly IQueryProvider _provider;
        private readonly SeekFilter _seekFilter;
        private readonly SeekQueryExpression? _seek;

        public RewriteSeekQueryVisitor(IQueryProvider provider, SeekFilter seekFilter, SeekQueryExpression? seek)
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
            var direction = _seek?.Direction;
            var origin = _seek?.Origin;

            var source = node.Arguments[0];

            var query = _provider.CreateQuery(source);
            var filter = _seekFilter.CreateFilter(source, direction, origin);

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
