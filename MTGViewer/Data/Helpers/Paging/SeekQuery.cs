using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

namespace System.Paging;


public class SeekQuery<TEntity, TKey>
{
    private readonly SeekInfo _seekInfo;

    private readonly IQueryable<TEntity> _query;
    private readonly Func<TEntity, TKey> _getKey;

    public SeekQuery(IQueryable<TEntity> query, Func<TEntity, TKey> getKey)
    {
        if (GetSeekInfo.Visit(query.Expression)
            is not ConstantExpression constant
            || constant.Value is not SeekInfo seekInfo)
        {
            throw new ArgumentException($"{nameof(query)} must have a \"Take\"");
        }

        _seekInfo = seekInfo;
        _query = query;
        _getKey = getKey;
    }


    private static GetSeekInfoVisitor? _getSeekInfo;
    private static ExpressionVisitor GetSeekInfo => _getSeekInfo ??= new();

    private static RemoveSeekOffsetVisitor? _lookAhead;
    private static ExpressionVisitor RemoveSeekOffset => _lookAhead ??= new();


    public async Task<SeekList<TEntity>> ToSeekListAsync(CancellationToken cancel = default)
    {
        var items = await _query
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        (SeekDirection direction, bool hasOrigin, int size) = _seekInfo;

        bool lookAhead = await _query
            .Visit(RemoveSeekOffset)
            .Skip(size)
            .AnyAsync(cancel)
            .ConfigureAwait(false);

        var seek = new Seek(items.Select(_getKey), direction, hasOrigin, lookAhead);

        return new SeekList<TEntity>(seek, items);
    }


    private record SeekInfo(SeekDirection Direction, bool HasOrigin, int Size);


    private class GetSeekInfoVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
            {
                return node;
            }

            if (node.Method == ExpressionConstants.QueryableWhere.MakeGenericMethod(typeof(TEntity))
                && node.Arguments.ElementAtOrDefault(1) is UnaryExpression quote
                && quote.NodeType is ExpressionType.Quote
                && OriginFilterVisitor.Instance.Visit(quote.Operand)
                is not DefaultExpression)
            {
                return ExpressionConstants.Null;
            }

            var visitedParent = Visit(parent);

            if (node.Method == ExpressionConstants.QueryableTake.MakeGenericMethod(typeof(TEntity))
                && node.Arguments.ElementAtOrDefault(1) is ConstantExpression takeBy
                && takeBy.Value is int take)
            {
                bool hasOrigin = visitedParent
                    is ConstantExpression origin && origin.Value is null;

                return Expression.Constant(
                    new SeekInfo(SeekDirection.Forward, hasOrigin, take));
            }

            if (node.Method != ExpressionConstants.QueryableReverse.MakeGenericMethod(typeof(TEntity)))
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
            if (node.Parameters.ElementAtOrDefault(0)?.Type != typeof(TEntity))
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


        private static OriginFilterVisitor? _instance;
        public static ExpressionVisitor Instance => _instance ??= new();
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

            if (node.Method == ExpressionConstants.QueryableTake.MakeGenericMethod(typeof(TEntity)))
            {
                return Visit(parent);
            }

            if (node.Method == ExpressionConstants.QueryableReverse.MakeGenericMethod(typeof(TEntity)))
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

            if (node.Method == ExpressionConstants.QueryableTake.MakeGenericMethod(typeof(TEntity)))
            {
                return Visit(parent);
            }

            return base.VisitMethodCall(node);
        }
    }

}