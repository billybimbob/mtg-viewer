using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

using NuGet.Packaging;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class FindNullPropertiesVisitor : ExpressionVisitor
{
    private readonly OrderByPropertyVisitor _orderProperty;

    public FindNullPropertiesVisitor(ParameterExpression parameter)
    {
        _orderProperty = new OrderByPropertyVisitor(parameter);
    }

    public IReadOnlyDictionary<MemberExpression, NullOrder> ScanProperties(Expression node)
    {
        if (Visit(node) is not ConstantExpression { Value: IReadOnlyDictionary<MemberExpression, NullOrder> properties })
        {
            properties = new Dictionary<MemberExpression, NullOrder>();
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

        var properties = new Dictionary<MemberExpression, NullOrder>();

        if (!ExpressionHelpers.IsOrderBy(node)
            && Visit(parent) is ConstantExpression { Value: IEnumerable<KeyValuePair<MemberExpression, NullOrder>> parentNulls })
        {
            properties.AddRange(parentNulls);
        }

        if (_orderProperty.Visit(node) is MemberExpression nullOrder)
        {
            var ordering = ExpressionHelpers.IsDescending(node)
                ? NullOrder.Before
                : NullOrder.After;

            properties.Add(nullOrder, ordering);
        }

        return Expression.Constant(properties);
    }
}
