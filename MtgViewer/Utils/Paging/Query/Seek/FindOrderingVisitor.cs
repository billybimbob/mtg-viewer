using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Seek;

internal class FindOrderingVisitor : ExpressionVisitor
{
    private static FindOrderingVisitor? _instance;
    public static FindOrderingVisitor Instance => _instance ??= new();

    private bool _foundSeek;

    [return: NotNullIfNotNull("node")]
    public override Expression? Visit(Expression? node)
    {
        _foundSeek = SeekByNotCalled(node);

        return base.Visit(node);
    }

    private static bool SeekByNotCalled(Expression? node)
        => FindSeekByVisitor.Instance.Visit(node) is not MethodCallExpression call
            || ExpressionHelpers.IsSeekBy(call) is false;

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsOrderedMethod(node))
        {
            if (!_foundSeek)
            {
                return node;
            }

            return base.Visit(node.Arguments[1]);
        }

        if (ExpressionHelpers.IsSeekBy(node))
        {
            _foundSeek = true;
        }

        if (node.Arguments.ElementAtOrDefault(0) is Expression parent)
        {
            return base.Visit(parent);
        }

        return node;
    }

    protected override Expression VisitLambda<TFunc>(Expression<TFunc> node)
    {
        if (node.Parameters.Count == 1)
        {
            return node.Parameters[0];
        }

        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType is ExpressionType.Quote)
        {
            return base.Visit(node.Operand);
        }

        return node;
    }

    private sealed class FindSeekByVisitor : ExpressionVisitor
    {
        private static FindSeekByVisitor? _instance;
        public static FindSeekByVisitor Instance => _instance ??= new();

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (ExpressionHelpers.IsSeekBy(node))
            {
                return node;
            }

            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
            {
                return node;
            }

            return Visit(parent);
        }
    }
}
