
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class OrderByNullPropertyVisitor : ExpressionVisitor
{
    private readonly ParameterExpression _parameter;

    public OrderByNullPropertyVisitor(ParameterExpression parameter)
    {
        _parameter = parameter;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsOrderedMethod(node))
        {
            return Visit(node.Arguments[1]);
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

    protected override Expression VisitLambda<TFunc>(Expression<TFunc> node)
    {
        if (node.Parameters.Count == 1 && node.Parameters[0].Type == _parameter.Type && node.Body is BinaryExpression)
        {
            return Visit(node.Body);
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

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (node.Type == _parameter.Type)
        {
            return _parameter;
        }

        return node;
    }
}