using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class SeekOrderCollection
{
    public ParameterExpression Parameter { get; }
    public IReadOnlyList<LinkedOrderProperty> OrderProperties { get; }

    private SeekOrderCollection(ParameterExpression parameter, IReadOnlyList<LinkedOrderProperty> orderProperties)
    {
        Parameter = parameter;
        OrderProperties = orderProperties;
    }

    public static SeekOrderCollection Build(ConstantExpression origin, Expression query)
    {
        var parameter = Expression
            .Parameter(
                origin.Type,
                origin.Type.Name[0].ToString().ToLowerInvariant());

        var findOrderProperties = new FindOrderPropertiesVisitor(parameter);
        var orderProperties = findOrderProperties.ScanProperties(query);

        var linkedProperties = CreateLinkedProperties(orderProperties);

        return new SeekOrderCollection(parameter, linkedProperties);
    }

    private static IReadOnlyList<LinkedOrderProperty> CreateLinkedProperties(IReadOnlyList<OrderProperty> sourceProperties)
    {
        if (sourceProperties.Count is 0)
        {
            return Array.Empty<LinkedOrderProperty>();
        }

        var linkProperties = new List<LinkedOrderProperty>(sourceProperties.Count);

        LinkedOrderProperty? previousLink = null;

        foreach (var property in sourceProperties)
        {
            var currentLink = new LinkedOrderProperty(property, previousLink);

            linkProperties.Add(currentLink);
            previousLink = currentLink;
        }

        return linkProperties;
    }
}
