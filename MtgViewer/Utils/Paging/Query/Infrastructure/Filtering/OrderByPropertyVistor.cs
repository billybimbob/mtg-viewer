using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class OrderByPropertyVisitor : ExpressionVisitor
{
    private readonly FindOrderMemberVisitor _findOrderMember;

    public OrderByPropertyVisitor(ReplaceParameterVisitor replaceParameter)
    {
        _findOrderMember = new FindOrderMemberVisitor(replaceParameter);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (!ExpressionHelpers.IsOrderedMethod(node))
        {
            return node;
        }

        if (_findOrderMember.Visit(node.Arguments[1]) is not MemberExpression member)
        {
            return node;
        }

        var ordering = ExpressionHelpers.IsDescending(node)
            ? Ordering.Descending
            : Ordering.Ascending;

        return Expression.Constant(new OrderProperty(member, ordering, NullOrder.None));
    }

    private sealed class FindOrderMemberVisitor : ExpressionVisitor
    {
        private readonly ReplaceParameterVisitor _replaceParameter;

        public FindOrderMemberVisitor(ReplaceParameterVisitor replaceParameter)
        {
            _replaceParameter = replaceParameter;
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
            if (node.Parameters.Count == 1)
            {
                return _replaceParameter.Visit(node.Body);
            }

            return node;
        }
    }
}
