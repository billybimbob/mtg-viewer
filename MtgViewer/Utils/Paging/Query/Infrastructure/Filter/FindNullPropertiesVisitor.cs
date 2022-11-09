using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;
using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filter;

internal sealed class FindNullPropertiesVisitor : ExpressionVisitor
{
    public static IReadOnlyDictionary<Expression, NullOrder> Scan(
        OrderPropertyVisitor orderByVisitor,
        OriginTranslator origin,
        Expression expression)
    {
        var findNulls = new FindNullPropertiesVisitor(orderByVisitor, origin);

        _ = findNulls.Visit(expression);

        return findNulls._properties;
    }

    private readonly OrderPropertyVisitor _orderProperty;
    private readonly OriginTranslator _origin;

    private readonly Dictionary<Expression, NullOrder> _properties;

    private MemberExpression? _lastProperty;

    private FindNullPropertiesVisitor(OrderPropertyVisitor orderProperty, OriginTranslator origin)
    {
        _orderProperty = orderProperty;
        _origin = origin;

        _properties = new Dictionary<Expression, NullOrder>(ExpressionEqualityComparer.Instance);
    }

    [return: NotNullIfNotNull(nameof(node))]
    public override Expression? Visit(Expression? node)
    {
        _lastProperty = null;
        _properties.Clear();

        return base.Visit(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (!ExpressionHelpers.IsOrderBy(node) && node.Arguments is [Expression parent, ..])
        {
            _ = base.Visit(parent);
        }

        if (_orderProperty.Visit(node) is not MemberExpression nullOrder
            || !_origin.TryRegister(nullOrder))
        {
            return node;
        }

        // a null is only used as a sort property if there are also
        // non null orderings specified

        // null check ordering by itself it not unique enough

        if (!ExpressionHelpers.IsDescendant(_lastProperty, nullOrder))
        {
            _lastProperty = nullOrder;

            return node;
        }

        var ordering = ExpressionHelpers.IsDescending(node)
            ? NullOrder.Before
            : NullOrder.After;

        _properties.Add(nullOrder, ordering);

        return node;
    }
}
