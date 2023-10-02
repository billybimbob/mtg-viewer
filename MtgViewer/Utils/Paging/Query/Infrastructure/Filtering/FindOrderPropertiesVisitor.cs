using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class FindOrderPropertiesVisitor : ExpressionVisitor
{
    private readonly OrderByPropertyVisitor _orderProperty;

    public FindOrderPropertiesVisitor(OrderByPropertyVisitor orderProperty)
    {
        _orderProperty = orderProperty;
    }

    public IReadOnlyList<KeyOrder> ScanProperties(Expression node)
    {
        if (Visit(node) is not ConstantExpression { Value: IReadOnlyList<KeyOrder> properties })
        {
            properties = Array.Empty<KeyOrder>();
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

        var properties = new List<KeyOrder>();

        if (!ExpressionHelpers.IsOrderBy(node)
            && Visit(parent) is ConstantExpression { Value: IEnumerable<KeyOrder> parentKeys })
        {
            properties.AddRange(parentKeys);
        }

        if (_orderProperty.Visit(node) is MemberExpression propertyOrder)
        {
            var ordering = ExpressionHelpers.IsDescending(node)
                ? Ordering.Descending
                : Ordering.Ascending;

            properties.Add(new KeyOrder(propertyOrder, ordering));
        }

        return Expression.Constant(properties);
    }
}
