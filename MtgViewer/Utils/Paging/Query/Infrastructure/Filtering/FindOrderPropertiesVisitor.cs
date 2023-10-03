using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class FindOrderPropertiesVisitor : ExpressionVisitor
{
    private readonly OrderByPropertyVisitor _orderByProperty;

    public FindOrderPropertiesVisitor(OrderByPropertyVisitor orderByProperty)
    {
        _orderByProperty = orderByProperty;
    }

    public IReadOnlyList<OrderProperty> ScanProperties(Expression node)
    {
        if (Visit(node) is not ConstantExpression { Value: IReadOnlyList<OrderProperty> properties })
        {
            properties = Array.Empty<OrderProperty>();
        }

        return properties;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        if (!ExpressionHelpers.IsOrderedMethod(node))
        {
            return Visit(parent);
        }

        var properties = new List<OrderProperty>();

        if (!ExpressionHelpers.IsOrderBy(node)
            && Visit(parent) is ConstantExpression { Value: IEnumerable<OrderProperty> parentProperties })
        {
            properties.AddRange(parentProperties);
        }

        if (_orderByProperty.Visit(node) is MemberExpression orderMember)
        {
            var ordering = ExpressionHelpers.IsDescending(node)
                ? Ordering.Descending
                : Ordering.Ascending;

            var nullOrder = GetNullOrder(orderMember, node);

            properties.Add(new OrderProperty(orderMember, ordering, nullOrder));
        }

        return Expression.Constant(properties);
    }

    private static NullOrder GetNullOrder(MemberExpression orderMember, MethodCallExpression node)
    {
        if (orderMember.Type.IsValueType && Nullable.GetUnderlyingType(orderMember.Type) == null)
        {
            return NullOrder.None;
        }

        return ExpressionHelpers.IsDescending(node)
            ? NullOrder.Before
            : NullOrder.After;
    }
}
