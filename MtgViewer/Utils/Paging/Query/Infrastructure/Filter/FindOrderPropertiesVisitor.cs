using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filter;

internal sealed class FindOrderPropertiesVisitor : ExpressionVisitor
{
    public static IReadOnlyList<KeyOrder> Scan(
        OrderPropertyVisitor orderByVisitor,
        OriginTranslator origin,
        Expression expression)
    {
        var findOrders = new FindOrderPropertiesVisitor(orderByVisitor, origin);

        _ = findOrders.Visit(expression);

        return findOrders._properties;
    }

    private readonly OrderPropertyVisitor _orderProperty;
    private readonly OriginTranslator _origin;

    private readonly List<KeyOrder> _properties;

    private FindOrderPropertiesVisitor(OrderPropertyVisitor orderProperty, OriginTranslator origin)
    {
        _orderProperty = orderProperty;
        _origin = origin;

        _properties = new List<KeyOrder>();
    }

    [return: NotNullIfNotNull(nameof(node))]
    public override Expression? Visit(Expression? node)
    {
        _properties.Clear();

        return base.Visit(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (!ExpressionHelpers.IsOrderBy(node) && node.Arguments is [Expression parent, ..])
        {
            _ = base.Visit(parent);
        }

        if (_orderProperty.Visit(node) is MemberExpression propertyOrder
            && _origin.TryRegister(propertyOrder))
        {
            var ordering = ExpressionHelpers.IsDescending(node)
                ? Ordering.Descending
                : Ordering.Ascending;

            _properties.Add(new KeyOrder(propertyOrder, ordering));
        }

        return node;
    }
}
