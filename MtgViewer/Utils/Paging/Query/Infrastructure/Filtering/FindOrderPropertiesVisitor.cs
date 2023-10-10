using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class FindOrderPropertiesVisitor : ExpressionVisitor
{
    private readonly OrderByPropertyVisitor _orderByProperty;
    private readonly NullOrderByPropertyVisitor _nullOrderByProperty;

    public FindOrderPropertiesVisitor(ReplaceParameterVisitor replaceParameter, EvaluateMemberVisitor evaluateMember)
    {
        _orderByProperty = new OrderByPropertyVisitor(replaceParameter);
        _nullOrderByProperty = new NullOrderByPropertyVisitor(replaceParameter, evaluateMember);
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

        if (GetOrderProperties(parent, node.Method) is { Count: > 0 } parentProperties)
        {
            properties.AddRange(parentProperties);
        }

        if (GetOrderProperty(node) is OrderProperty property)
        {
            properties.Add(property);
        }

        return Expression.Constant(properties);
    }

    private IReadOnlyList<OrderProperty> GetOrderProperties(Expression caller, MethodInfo method)
    {
        if (ExpressionHelpers.IsThenBy(method)
            && Visit(caller) is ConstantExpression { Value: IReadOnlyList<OrderProperty> properties })
        {
            return properties;
        }

        return Array.Empty<OrderProperty>();
    }

    private OrderProperty? GetOrderProperty(MethodCallExpression node)
    {
        if (_orderByProperty.Visit(node) is ConstantExpression { Value: OrderProperty orderProperty })
        {
            return orderProperty;
        }

        if (_nullOrderByProperty.Visit(node) is ConstantExpression { Value: OrderProperty nullProperty })
        {
            return nullProperty;
        }

        return null;
    }
}
