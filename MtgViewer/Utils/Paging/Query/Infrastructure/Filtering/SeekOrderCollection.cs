using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class SeekOrderCollection
{
    private readonly OriginTranslator _origin;
    private readonly IReadOnlyList<OrderProperty> _orderProperties;
    private readonly ParameterExpression _parameter;

    private SeekOrderCollection(
        OriginTranslator origin,
        IReadOnlyList<OrderProperty> orderKeys,
        ParameterExpression parameter)
    {
        _origin = origin;
        _orderProperties = orderKeys;
        _parameter = parameter;
    }

    public static SeekOrderCollection Build(ConstantExpression origin, Expression query)
    {
        var parameter = Expression
            .Parameter(
                origin.Type,
                origin.Type.Name[0].ToString().ToLowerInvariant());

        var orderProperty = new OrderByPropertyVisitor(parameter);
        var findOrderProperties = new FindOrderPropertiesVisitor(orderProperty);

        var orderProperties = findOrderProperties.ScanProperties(query);
        var translations = orderProperties
            .Select(o => o.Member)
            .OfType<MemberExpression>()
            .ToList();

        var originTranslator = OriginTranslator.Build(origin, translations);

        return new SeekOrderCollection(originTranslator, orderProperties, parameter);
    }

    public IReadOnlyList<LinkedOrderProperty> BuildOrderProperties()
    {
        if (_orderProperties.Count is 0)
        {
            return Array.Empty<LinkedOrderProperty>();
        }

        LinkedOrderProperty? previousLink = null;

        var filterProperties = new List<LinkedOrderProperty>(_orderProperties.Count);

        foreach (var property in _orderProperties)
        {
            var currentLink = new LinkedOrderProperty(property, previousLink);

            filterProperties.Add(currentLink);
            previousLink = currentLink;
        }

        return filterProperties;
    }

    public LambdaExpression BuildLambdaFilter(BinaryExpression filterBody)
        => Expression.Lambda(filterBody, _parameter);

    public MemberExpression? Translate(MemberExpression node)
        => _origin.Translate(node);

    public bool IsMemberNull(MemberExpression node)
        => _origin.IsMemberNull(node);
}
