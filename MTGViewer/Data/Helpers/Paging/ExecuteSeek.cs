using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

internal static class ExecuteSeek<TEntity> where TEntity : class
{
    public static async Task<SeekList<TEntity>> ToSeekListAsync(
        IQueryable<TEntity> query,
        CancellationToken cancel = default)
    {
        if (GetSeekInfoVisitor.Instance.Visit(query.Expression)
            is not ConstantExpression { Value: SeekInfo seekInfo })
        {
            throw new ArgumentException($"{nameof(query)} missing seek values");
        }

        var items = await query
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        (SeekDirection direction, bool hasOrigin, int? size) = seekInfo;

        var withoutOffset = query.Provider
            .CreateQuery<TEntity>(RemoveSeekOffsetVisitor.Instance.Visit(query.Expression));

        bool lookAhead = size is not int s
            ? false
            : await withoutOffset
                .Skip(s)
                .AnyAsync(cancel)
                .ConfigureAwait(false);

        var seek = new Seek<TEntity>(items, direction, hasOrigin, lookAhead);

        return new SeekList<TEntity>(seek, items);
    }


    private record SeekInfo(SeekDirection Direction, bool HasOrigin, int? Size);


    private class GetSeekInfoVisitor : ExpressionVisitor
    {
        private static GetSeekInfoVisitor? _instance;
        public static ExpressionVisitor Instance => _instance ??= new();


        [return: NotNullIfNotNull("node")]
        public override Expression? Visit(Expression? node)
        {
            if (node is QueryRootExpression)
            {
                return Expression.Constant(
                    new SeekInfo(SeekDirection.Forward, false, null));
            }

            return base.Visit(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent
                || node is { Method.IsGenericMethod: false })
            {
                return node;
            }

            var method = node.Method.GetGenericMethodDefinition();

            if (method == QueryableMethods.Where
                && node.Arguments.ElementAtOrDefault(1) is var filter
                && OriginFilterVisitor.Instance.Visit(filter) is not DefaultExpression)
            {
                return Expression.Constant(
                    new SeekInfo(SeekDirection.Forward, true, null));
            }

            if (Visit(parent) is not ConstantExpression seekParent
                || seekParent is not { Value: SeekInfo seekInfo })
            {
                return node;
            }

            if (method == QueryableMethods.Take
                && node.Arguments.ElementAtOrDefault(1) is ConstantExpression { Value: int take })
            {
                return Expression.Constant(
                    seekInfo with { Size = take });
            }

            if (method == QueryableMethods.Reverse)
            {
                return Expression.Constant(
                    seekInfo with { Direction = SeekDirection.Backwards });
            }

            return seekParent;
        }
    }


    private class OriginFilterVisitor : ExpressionVisitor
    {
        private static OriginFilterVisitor? _instance;
        public static ExpressionVisitor Instance => _instance ??= new();

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType is not ExpressionType.Quote)
            {
                return node;
            }

            return Visit(node.Operand);
        }

        protected override Expression VisitLambda<TFunc>(Expression<TFunc> node)
        {
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
            return operand is MemberExpression or ConstantExpression or BinaryExpression
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
        private static RemoveSeekOffsetVisitor? _instance;
        public static ExpressionVisitor Instance => _instance ??= new();


        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent
                || node is { Method.IsGenericMethod: false })
            {
                return node;
            }

            var method = node.Method.GetGenericMethodDefinition();

            if (method == QueryableMethods.Reverse)
            {
                return ReversedLookAheadVisitor.Instance.Visit(parent);
            }

            if (method == QueryableMethods.Take)
            {
                return parent;
            }

            return node.Update(
                node.Object,
                node.Arguments.Skip(1).Prepend(Visit(parent)));
        }
    }


    private class ReversedLookAheadVisitor : ExpressionVisitor
    {
        private static ReversedLookAheadVisitor? _instance;
        public static ExpressionVisitor Instance => _instance ??= new();

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
                return parent;
            }

            return node.Update(
                node.Object,
                node.Arguments.Skip(1).Prepend(Visit(parent)));
        }
    }

}