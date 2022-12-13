using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal class FindOrderingVisitor : ExpressionVisitor
{
    public static FindOrderingVisitor Instance { get; } = new();

    private bool _foundSeek;

    private FindOrderingVisitor()
    {
    }

    [return: NotNullIfNotNull(nameof(node))]
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
            if (_foundSeek && node.Arguments is [_, var ordering])
            {
                return base.Visit(ordering);
            }
            else
            {
                return node;
            }
        }

        if (ExpressionHelpers.IsSeekBy(node))
        {
            _foundSeek = true;
        }

        if (node.Arguments is [Expression parent, ..])
        {
            return base.Visit(parent);
        }

        return node;
    }

    protected override Expression VisitLambda<TFunc>(Expression<TFunc> node)
    {
        if (node.Parameters is [Expression p])
        {
            return p;
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
        public static FindSeekByVisitor Instance { get; } = new();

        private FindSeekByVisitor()
        {
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (ExpressionHelpers.IsSeekBy(node))
            {
                return node;
            }

            if (node.Arguments is not [Expression parent, ..])
            {
                return node;
            }

            return Visit(parent);
        }
    }
}
