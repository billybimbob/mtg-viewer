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

        int pageSize = offsetExpression.Size ?? totalItems - offsetExpression.Index;

        var offset = new Offset(offsetExpression.Index, totalItems, pageSize);

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

        int pageSize = offsetExpression.Size ?? totalItems - offsetExpression.Index;

        var offset = new Offset(offsetExpression.Index, totalItems, pageSize);

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
        public OffsetExpression(int index, int? size)
        {
            Index = index;
            Size = size;
        }

        public int Index { get; }

        public int? Size { get; }

        public override Type Type => typeof(Offset);

        public override ExpressionType NodeType => ExpressionType.Extension;

        public override bool CanReduce => false;

        protected override Expression VisitChildren(ExpressionVisitor visitor) => this;

        public OffsetExpression Update(int index, int? size)
        {
            return index == Index && size == Size
                ? this
                : new OffsetExpression(index, size);
        }
    }

    private sealed class FindOffsetVisitor : ExpressionVisitor
    {
        private static FindOffsetVisitor? _instance;
        public static FindOffsetVisitor Instance => _instance ??= new();

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
                return offset.Update(offset.Index, size);
            }

            return new OffsetExpression(0, size);
        }
    }

    private sealed class RemoveOffsetVisitor : ExpressionVisitor
    {
        private static RemoveOffsetVisitor? _instance;
        public static RemoveOffsetVisitor Instance => _instance ??= new();

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent
                || node is { Method.IsGenericMethod: false })
            {
                return node;
            }

            var method = node.Method.GetGenericMethodDefinition();

            var visitedParent = Visit(parent);

            if (method == QueryableMethods.Take)
            {
                return visitedParent;
            }

            if (method == QueryableMethods.Skip)
            {
                return visitedParent;
            }

            return node.Update(
                node.Object,
                node.Arguments.Skip(1).Prepend(visitedParent));
        }
    }
}
