using System;
using System.Linq.Expressions;
using System.Reflection;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class NullOrderByPropertyVisitor : ExpressionVisitor
{
    private readonly FindOrderMemberVisitor _findOrderMember;

    public NullOrderByPropertyVisitor(ReplaceParameterVisitor replaceParameter, EvaluateMemberVisitor evaluateMember)
    {
        _findOrderMember = new FindOrderMemberVisitor(replaceParameter, evaluateMember);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (!ExpressionHelpers.IsOrderedMethod(node))
        {
            return node;
        }

        if (_findOrderMember.Visit(node.Arguments[1]) is not MemberExpression orderMember)
        {
            return node;
        }

        var ordering = ExpressionHelpers.IsDescending(node)
            ? Ordering.Descending
            : Ordering.Ascending;

        var nullOrder = GetNullOrder(orderMember, node.Method);

        return Expression.Constant(new OrderProperty(orderMember, ordering, nullOrder));
    }

    private static NullOrder GetNullOrder(MemberExpression orderMember, MethodInfo method)
    {
        if (orderMember.Type.IsValueType && Nullable.GetUnderlyingType(orderMember.Type) == null)
        {
            return NullOrder.None;
        }

        return ExpressionHelpers.IsDescending(method)
            ? NullOrder.Before
            : NullOrder.After;
    }

    private sealed class FindOrderMemberVisitor : ExpressionVisitor
    {
        private readonly ReplaceParameterVisitor _replaceParameter;
        private readonly EvaluateMemberVisitor _evaluateMember;

        public FindOrderMemberVisitor(ReplaceParameterVisitor replaceParameter, EvaluateMemberVisitor evaluateMember)
        {
            _replaceParameter = replaceParameter;
            _evaluateMember = evaluateMember;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType is ExpressionType.Quote)
            {
                return Visit(node.Operand);
            }

            return node;
        }

        protected override Expression VisitLambda<TFunc>(Expression<TFunc> node)
        {
            if (node.Parameters.Count == 1)
            {
                return Visit(node.Body);
            }

            return node;
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is ExpressionType.Equal
                && _replaceParameter.Visit(node.Left) is MemberExpression left
                && _evaluateMember.Visit(node.Right) is ConstantExpression { Value: null })
            {
                return left;
            }

            return node;
        }
    }
}
