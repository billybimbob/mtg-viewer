using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

namespace System.Paging;

internal class OffsetQuery<TResult>
{
    private readonly IQueryable<TResult> _query;
    private readonly PageInfo _pageInfo;


    public OffsetQuery(IQueryable<TResult> query)
    {
        if (GetPageInfo.Visit(query.Expression)
            is not ConstantExpression constant
            || constant.Value is not PageInfo pageInfo)
        {
            throw new ArgumentException(
                $"{nameof(query)} must have a \"Skip\" followed by a \"Take\"");
        }

        _query = query;
        _pageInfo = pageInfo;
    }


    private static GetPageInfoVisitor? _getPageInfo;
    private static ExpressionVisitor GetPageInfo => _getPageInfo ??= new();

    private static RemoveOffsetVisitor? _removeOffset;
    private static ExpressionVisitor RemoveOffset => _removeOffset ??= new();


    public OffsetList<TResult> ToOffsetList()
    {
        int totalItems = _query
            .Visit(RemoveOffset)
            .Count();

        (int index, int size) = _pageInfo;

        var offset = new Offset(index, totalItems, size);

        var items = _query.ToList();

        return new OffsetList<TResult>(offset, items);
    }


    public async Task<OffsetList<TResult>> ToOffsetListAsync(CancellationToken cancel = default)
    {
        int totalItems = await _query
            .Visit(RemoveOffset)
            .CountAsync(cancel)
            .ConfigureAwait(false);

        (int index, int size) = _pageInfo;

        var offset = new Offset(index, totalItems, size);

        var items = await _query
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        return new OffsetList<TResult>(offset, items);
    }


    private record PageInfo(int Index, int Size);


    private class GetPageInfoVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.GetGenericMethodDefinition() == ExpressionConstants.QueryableSkip
                && node.Arguments.ElementAtOrDefault(1) is ConstantExpression skip)
            {
                return skip;
            }

            if (node.Method.GetGenericMethodDefinition() == ExpressionConstants.QueryableTake

                && node.Arguments.ElementAtOrDefault(1)
                    is ConstantExpression take

                && Visit(node.Arguments.ElementAtOrDefault(0))
                    is ConstantExpression innerSkip

                && innerSkip.Value is int skipBy
                && take.Value is int pageSize)
            {
                return Expression.Constant(new PageInfo(skipBy / pageSize, pageSize));
            }

            if (node.Arguments.ElementAtOrDefault(0) is Expression caller)
            {
                return Visit(caller);
            }

            return base.VisitMethodCall(node);
        }
    }


    private class RemoveOffsetVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
            {
                return node;
            }

            if (node.Method.GetGenericMethodDefinition() == ExpressionConstants.QueryableTake)
            {
                return Visit(parent);
            }

            if (node.Method.GetGenericMethodDefinition() == ExpressionConstants.QueryableSkip)
            {
                return parent;
            }

            if (node.Object is not null)
            {
                return node;
            }

            return base.VisitMethodCall(node);
        }
    }
}
