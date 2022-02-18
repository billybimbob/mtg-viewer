using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace System.Paging;


public class SeekQuery<T>
{
    private readonly IQueryable<T> _source;
    private readonly SeekInfo _seekInfo;

    public SeekQuery(IQueryable<T> source)
    {
        if (GetSeekInfo.Visit(source.Expression)
            is not ConstantExpression constant
            || constant.Value is not SeekInfo seekInfo)
        {
            throw new ArgumentException($"{nameof(source)} must have a \"Take\"");
        }

        _seekInfo = seekInfo;
        _source = source;
    }


    private static GetSeekInfoVisitor? _getSeekInfo;
    private static ExpressionVisitor GetSeekInfo => _getSeekInfo ??= new();

    private static RemoveSeekOffsetVisitor? _lookAhead;
    private static ExpressionVisitor RemoveSeekOffset => _lookAhead ??= new();


    public async Task<SeekList<T>> ToSeekListAsync(CancellationToken cancel = default)
    {
        var items = await _source
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        (SeekDirection direction, bool hasOrigin, int size) = _seekInfo;

        bool lookAhead = await _source
            .Visit(RemoveSeekOffset)
            .Skip(size)
            .AnyAsync(cancel)
            .ConfigureAwait(false);

        var seek = new Seek<T>(items, direction, hasOrigin, lookAhead);

        return new SeekList<T>(seek, items);
    }


    private record SeekInfo(SeekDirection Direction, bool HasOrigin, int Size);


    private class GetSeekInfoVisitor : ExpressionVisitor
    {
        private static OriginFilterVisitor? _getOriginFilter;
        private static ExpressionVisitor GetOriginFilter => _getOriginFilter ??= new();

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
            {
                return node;
            }

            if (node.Method == ExpressionConstants.GetQueryableWhere<T>()
                && node.Arguments.ElementAtOrDefault(1) is UnaryExpression quote
                && quote.NodeType is ExpressionType.Quote
                && GetOriginFilter.Visit(quote.Operand) is not DefaultExpression)
            {
                return ExpressionConstants.Null;
            }

            var visitedParent = Visit(parent);

            if (node.Method == ExpressionConstants.GetQueryableTake<T>()
                && node.Arguments.ElementAtOrDefault(1) is ConstantExpression takeBy
                && takeBy.Value is int take)
            {
                bool hasOrigin = visitedParent
                    is ConstantExpression origin && origin.Value is null;

                return Expression.Constant(
                    new SeekInfo(SeekDirection.Forward, hasOrigin, take));
            }

            if (node.Method != ExpressionConstants.GetQueryableReverse<T>())
            {
                return visitedParent;
            }

            if (visitedParent is ConstantExpression innerTake
                && innerTake.Value is SeekInfo seekInfo)
            {
                return Expression.Constant(
                    seekInfo with { Direction = SeekDirection.Backwards });
            }

            return node;
        }
    }


    private class OriginFilterVisitor : ExpressionVisitor
    {
        protected override Expression VisitLambda<TFunc>(Expression<TFunc> node)
        {
            if (node.Parameters.ElementAtOrDefault(0)?.Type != typeof(T))
            {
                return Expression.Empty();
            }

            if (node.Body is not BinaryExpression or MethodCallExpression)
            {
                return Expression.Empty();
            }

            return Visit(node.Body);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType
                is ExpressionType.GreaterThan or ExpressionType.LessThan)
            {
                return node;
            }

            if (node.NodeType
                is ExpressionType.AndAlso or ExpressionType.OrElse
                && (IsValidBooleanChild(node.Left)
                    || IsValidBooleanChild(node.Right)))
            {
                return node;
            }

            return Expression.Empty();
        }

        private bool IsValidBooleanChild(Expression operand)
        {
            return operand is MemberExpression
                or ConstantExpression
                or BinaryExpression
                && Visit(operand) is not DefaultExpression;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method == ExpressionConstants.StringCompare)
            {
                return base.VisitMethodCall(node);
            }

            return Expression.Empty();
        }
    }


    private class RemoveSeekOffsetVisitor : ExpressionVisitor
    {
        private static ReversedLookAheadVisitor? _reverseVisitor;
        private static ExpressionVisitor ReverseVisitor => _reverseVisitor ??= new();

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

            if (node.Method == ExpressionConstants.GetQueryableReverse<T>())
            {
                return ReverseVisitor.Visit(parent);
            }

            return base.VisitMethodCall(node);
        }
    }


    private class ReversedLookAheadVisitor : ExpressionVisitor
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

            return base.VisitMethodCall(node);
        }
    }

}