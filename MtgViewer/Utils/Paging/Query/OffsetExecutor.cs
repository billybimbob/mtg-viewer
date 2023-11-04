using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal class OffsetExecutor<TEntity>
{
    private readonly FindOffsetVisitor _offsetFinder;
    private readonly RemoveOffsetVisitor _offsetRemover;

    public OffsetExecutor()
    {
        _offsetFinder = new FindOffsetVisitor();
        _offsetRemover = new RemoveOffsetVisitor();
    }

    public OffsetList<TEntity> Execute(IQueryable<TEntity> query)
    {
        var offsetExpression = _offsetFinder.Find(query.Expression);
        int totalItems = GetTotalItems(query);

        int pageSize = offsetExpression.Size ?? totalItems - offsetExpression.Skip;
        int currentPage = offsetExpression.Skip / pageSize;

        var offset = new Offset(currentPage, totalItems, pageSize);
        var items = query.ToList();

        return new OffsetList<TEntity>(offset, items);
    }

    private int GetTotalItems(IQueryable<TEntity> query)
    {
        var withoutOffset = _offsetRemover.Visit(query.Expression);

        return query.Provider
            .CreateQuery<TEntity>(withoutOffset)
            .Count();
    }

    public async Task<OffsetList<TEntity>> ExecuteAsync(IQueryable<TEntity> query, CancellationToken cancel)
    {
        var offsetExpression = _offsetFinder.Find(query.Expression);
        int totalItems = await GetTotalItemsAsync(query, cancel).ConfigureAwait(false);

        int pageSize = offsetExpression.Size ?? totalItems - offsetExpression.Skip;
        int currentPage = offsetExpression.Skip / pageSize;

        var offset = new Offset(currentPage, totalItems, pageSize);
        var items = await query.ToListAsync(cancel).ConfigureAwait(false);

        return new OffsetList<TEntity>(offset, items);
    }

    private async Task<int> GetTotalItemsAsync(IQueryable<TEntity> query, CancellationToken cancel)
    {
        var withoutOffset = _offsetRemover.Visit(query.Expression);

        return await query.Provider
            .CreateQuery<TEntity>(withoutOffset)
            .CountAsync(cancel)
            .ConfigureAwait(false);
    }

    private sealed class OffsetExpression : Expression
    {
        public OffsetExpression(int skip, int? size)
        {
            Skip = skip;
            Size = size;
        }

        public int Skip { get; }

        public int? Size { get; }

        public override Type Type => typeof(Offset);

        public override ExpressionType NodeType => ExpressionType.Extension;

        public override bool CanReduce => false;

        protected override Expression VisitChildren(ExpressionVisitor visitor) => this;

        public OffsetExpression Update(int skip, int? size)
        {
            return skip == Skip && size == Size
                ? this
                : new OffsetExpression(skip, size);
        }
    }

    private sealed class FindOffsetVisitor : ExpressionVisitor
    {
        public OffsetExpression Find(Expression node)
            => Visit(node) as OffsetExpression ?? new OffsetExpression(0, null);

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent
                || node is { Method.IsGenericMethod: false })
            {
                return node;
            }

            var method = node.Method.GetGenericMethodDefinition();

            if (method == QueryableMethods.Skip
                && node.Arguments.ElementAtOrDefault(1) is ConstantExpression { Value: int skip })
            {
                return new OffsetExpression(skip, null);
            }

            var visitedParent = Visit(parent);

            if (method != QueryableMethods.Take
                || node.Arguments.ElementAtOrDefault(1) is not ConstantExpression { Value: int size })
            {
                return visitedParent;
            }

            if (visitedParent is OffsetExpression offset)
            {
                return offset.Update(offset.Skip, size);
            }

            return new OffsetExpression(0, size);
        }
    }

    private sealed class RemoveOffsetVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent
                || node is { Method.IsGenericMethod: false })
            {
                return base.VisitMethodCall(node);
            }

            var method = node.Method.GetGenericMethodDefinition();

            if (method == QueryableMethods.Take)
            {
                return Visit(parent);
            }

            if (method == QueryableMethods.Skip)
            {
                return Visit(parent);
            }

            return base.VisitMethodCall(node);
        }
    }
}
