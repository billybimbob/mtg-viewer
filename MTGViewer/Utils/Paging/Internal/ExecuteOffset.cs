using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;


internal static class ExecuteOffset<TEntity>
{
    private static GetPageInfoVisitor? s_getPageInfo;
    private static ExpressionVisitor GetPageInfo => s_getPageInfo ??= new();

    private static RemoveOffsetVisitor? s_removeOffset;
    private static ExpressionVisitor RemoveOffset => s_removeOffset ??= new();


    public static OffsetList<TEntity> ToOffsetList(IQueryable<TEntity> query)
    {
        if (GetPageInfo.Visit(query.Expression)
            is not ConstantExpression { Value: PageInfo pageInfo })
        {
            throw new ArgumentException(
                "Missing expected \"Skip\" followed by a \"Take\"", nameof(query));
        }

        var withoutOffset = query.Provider
            .CreateQuery<TEntity>(RemoveOffset.Visit(query.Expression));

        int totalItems = withoutOffset.Count();

        (int index, int size) = pageInfo;

        var offset = new Offset(index, totalItems, size);
        var items = query.ToList();

        return new OffsetList<TEntity>(offset, items);
    }


    public static async Task<OffsetList<TEntity>> ToOffsetListAsync(
        IQueryable<TEntity> query,
        CancellationToken cancel = default)
    {
        if (GetPageInfo.Visit(query.Expression)
            is not ConstantExpression { Value: PageInfo pageInfo })
        {
            throw new ArgumentException(
                "Missing expected \"Skip\" followed by a \"Take\"", nameof(query));
        }

        var withoutOffset = query.Provider
            .CreateQuery<TEntity>(RemoveOffset.Visit(query.Expression));

        int totalItems = await withoutOffset
            .CountAsync(cancel)
            .ConfigureAwait(false);

        (int index, int size) = pageInfo;

        var offset = new Offset(index, totalItems, size);

        var items = await query
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        return new OffsetList<TEntity>(offset, items);
    }


    private record PageInfo(int Index, int Size);


    private class GetPageInfoVisitor : ExpressionVisitor
    {
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
                    is ConstantExpression { Value: int skipBy }

                && node.Arguments.ElementAtOrDefault(1)
                    is ConstantExpression { Value: int pageSize })
            {
                return Expression.Constant(new PageInfo(skipBy / pageSize, pageSize));
            }

            return visitedParent;
        }
    }


    private class RemoveOffsetVisitor : ExpressionVisitor
    {
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
