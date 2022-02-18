using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace System.Paging;

internal class OffsetQuery<T>
{
    private readonly IQueryable<T> _source;
    private readonly PageInfo _pageInfo;

    public OffsetQuery(IQueryable<T> source)
    {
        if (GetPageInfo.Visit(source.Expression)
            is not ConstantExpression constant
            || constant.Value is not PageInfo pageInfo)
        {
            throw new ArgumentException(
                $"{nameof(source)} must have a \"Skip\" followed by a \"Take\"");
        }

        _source = source;
        _pageInfo = pageInfo;
    }


    private static GetPageInfoVisitor? _getPageInfo;
    private static ExpressionVisitor GetPageInfo => _getPageInfo ??= new();

    private static RemoveOffsetVisitor? _removeOffset;
    private static ExpressionVisitor RemoveOffset => _removeOffset ??= new();


    public OffsetList<T> ToOffsetList()
    {
        int totalItems = _source
            .Visit(RemoveOffset)
            .Count();

        (int index, int size) = _pageInfo;

        var offset = new Offset(index, totalItems, size);

        var items = _source.ToList();

        return new OffsetList<T>(offset, items);
    }


    public async Task<OffsetList<T>> ToOffsetListAsync(CancellationToken cancel = default)
    {
        int totalItems = await _source
            .Visit(RemoveOffset)
            .CountAsync(cancel)
            .ConfigureAwait(false);

        (int index, int size) = _pageInfo;

        var offset = new Offset(index, totalItems, size);

        var items = await _source
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        return new OffsetList<T>(offset, items);
    }


    private record PageInfo(int Index, int Size);


    private class GetPageInfoVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method == ExpressionConstants.GetQueryableSkip<T>()
                && node.Arguments.ElementAtOrDefault(1) is ConstantExpression skip)
            {
                return skip;
            }

            if (node.Method == ExpressionConstants.GetQueryableTake<T>()

                && node.Arguments.ElementAtOrDefault(1)
                    is ConstantExpression take

                && Visit(node.Arguments.ElementAtOrDefault(0))
                    is ConstantExpression innerSkip

                && innerSkip.Value is int skipBy
                && take.Value is int pageSize)
            {
                return Expression.Constant(new PageInfo(skipBy / pageSize, pageSize));
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

            if (node.Method == ExpressionConstants.GetQueryableTake<T>())
            {
                return Visit(parent);
            }

            if (node.Method == ExpressionConstants.GetQueryableSkip<T>())
            {
                return Visit(parent);
            }

            return base.VisitMethodCall(node);
        }
    }
}
