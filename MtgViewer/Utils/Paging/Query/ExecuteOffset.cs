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
        if (FindOffsetVisitor.Instance.Visit(query.Expression) is not OffsetExpression offsetInfo)
        {
            throw new ArgumentException(
                "Missing expected \"Skip\" followed by a \"Take\"", nameof(query));
        }

        int totalItems = GetTotalItems(query);

        var offset = new Offset(offsetInfo.Index, totalItems, offsetInfo.Size);

        var items = query.ToList();

        return new OffsetList<TEntity>(offset, items);
    }

    private static int GetTotalItems(IQueryable<TEntity> query)
    {
        var withoutOffset = query.Provider
            .CreateQuery<TEntity>(RemoveOffsetVisitor.Instance.Visit(query.Expression));

        return withoutOffset.Count();
    }

    public static async Task<OffsetList<TEntity>> ToOffsetListAsync(
        IQueryable<TEntity> query,
        CancellationToken cancel = default)
    {
        if (FindOffsetVisitor.Instance.Visit(query.Expression) is not OffsetExpression offsetInfo)
        {
            throw new ArgumentException(
                "Missing expected \"Skip\" followed by a \"Take\"", nameof(query));
        }

        int totalItems = await GetTotalItemsAsync(query, cancel).ConfigureAwait(false);

        var offset = new Offset(offsetInfo.Index, totalItems, offsetInfo.Size);

        var items = await query.ToListAsync(cancel).ConfigureAwait(false);

        return new OffsetList<TEntity>(offset, items);
    }

    private static async Task<int> GetTotalItemsAsync(IQueryable<TEntity> query, CancellationToken cancel)
    {
        var withoutOffset = query.Provider
            .CreateQuery<TEntity>(RemoveOffsetVisitor.Instance.Visit(query.Expression));

        return await withoutOffset.CountAsync(cancel).ConfigureAwait(false);
    }

    private sealed class OffsetExpression : Expression
    {
        public OffsetExpression(int index, int size)
        {
            Index = index;
            Size = size;
        }

        public int Index { get; }

        public int Size { get; }

        public override Type Type => typeof(Offset);

        public override ExpressionType NodeType => ExpressionType.Extension;

        public override bool CanReduce => false;

        protected override Expression VisitChildren(ExpressionVisitor visitor) => this;
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
                && node.Arguments.ElementAtOrDefault(1) is ConstantExpression skip)
            {
                return skip;
            }

            var visitedParent = Visit(parent);

            if (method == QueryableMethods.Take
                && visitedParent
                    is ConstantExpression { Value: int index }

                && node.Arguments.ElementAtOrDefault(1)
                    is ConstantExpression { Value: int size })
            {
                return new OffsetExpression(index, size);
            }

            return visitedParent;
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

            if (method == QueryableMethods.Take)
            {
                return Visit(parent);
            }

            if (method == QueryableMethods.Skip)
            {
                return parent;
            }

            if (node.Object is not null)
            {
                return node;
            }

            return node.Update(
                node.Object,
                node.Arguments.Skip(1).Prepend(Visit(parent)));
        }
    }
}
