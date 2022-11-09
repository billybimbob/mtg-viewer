using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filter;

internal sealed class OrderPropertyVisitor : ExpressionVisitor
{
    private readonly ParameterExpression _parameter;

    public OrderPropertyVisitor(ParameterExpression parameter)
    {
        _parameter = parameter;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsOrderedMethod(node) && node.Arguments.Count == 2)
        {
            return Visit(node.Arguments[1]);
        }

        return node;
    }

    protected override Expression VisitLambda<TFunc>(Expression<TFunc> node)
    {
        if (node.Parameters is [ParameterExpression p] && p.Type == _parameter.Type)
        {
            return Visit(node.Body);
        }

        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType is not ExpressionType.Quote)
        {
            return node;
        }

        return Visit(node.Operand);
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (node.Type == _parameter.Type)
        {
            return _parameter;
        }

        return node;
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType is not ExpressionType.Equal)
        {
            return node;
        }

        if (ExpressionHelpers.IsNull(node.Right) && Visit(node.Left) is MemberExpression left)
        {
            return left;
        }

        if (ExpressionHelpers.IsNull(node.Left) && Visit(node.Right) is MemberExpression right)
        {
            return right;
        }

        return node;
    }
}
