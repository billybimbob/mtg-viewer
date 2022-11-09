using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal static class ExecuteOffset<TEntity>
{
    public static OffsetList<TEntity> ToOffsetList(IQueryable<TEntity> query)
    {
        if (FindOffsetVisitor.Instance.Visit(query.Expression) is not OffsetExpression offsetExpression)
        {
            offsetExpression = new OffsetExpression(0, null);
        }

        int totalItems = GetTotalItems(query);

        int pageSize = offsetExpression.Size ?? totalItems - offsetExpression.Skip;

        int currentPage = offsetExpression.Skip / pageSize;

        var offset = new Offset(currentPage, totalItems, pageSize);

        var items = query.ToList();

        return new OffsetList<TEntity>(offset, items);
    }

    private static int GetTotalItems(IQueryable<TEntity> query)
    {
        var withoutOffset = RemoveOffsetVisitor.Instance.Visit(query.Expression);

        return query.Provider
            .CreateQuery<TEntity>(withoutOffset)
            .Count();
    }

    public static async Task<OffsetList<TEntity>> ToOffsetListAsync(IQueryable<TEntity> query, CancellationToken cancel)
    {
        if (FindOffsetVisitor.Instance.Visit(query.Expression) is not OffsetExpression offsetExpression)
        {
            offsetExpression = new OffsetExpression(0, null);
        }

        int totalItems = await GetTotalItemsAsync(query, cancel).ConfigureAwait(false);

        int pageSize = offsetExpression.Size ?? totalItems - offsetExpression.Skip;

        int currentPage = offsetExpression.Skip / pageSize;

        var offset = new Offset(currentPage, totalItems, pageSize);

        var items = await query.ToListAsync(cancel).ConfigureAwait(false);

        return new OffsetList<TEntity>(offset, items);
    }

    private static async Task<int> GetTotalItemsAsync(IQueryable<TEntity> query, CancellationToken cancel)
    {
        var withoutOffset = RemoveOffsetVisitor.Instance.Visit(query.Expression);

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
        public static FindOffsetVisitor Instance { get; } = new();

        private FindOffsetVisitor()
        {
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node is not { Method.IsGenericMethod: true, Arguments: [Expression parent, ..] })
            {
                return node;
            }

            var method = node.Method.GetGenericMethodDefinition();

            if (method == QueryableMethods.Skip
                && node.Arguments is [_, ConstantExpression { Value: int skip }])
            {
                return new OffsetExpression(skip, null);
            }

            var visitedParent = Visit(parent);

            if (method != QueryableMethods.Take
                || node.Arguments is not [_, ConstantExpression { Value: int size }])
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
        public static RemoveOffsetVisitor Instance { get; } = new();

        private RemoveOffsetVisitor()
        {
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node is not { Method.IsGenericMethod: true, Arguments: [Expression parent, ..] })
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
