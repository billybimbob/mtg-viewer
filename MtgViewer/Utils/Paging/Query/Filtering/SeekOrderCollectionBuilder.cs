using System;
using System.Collections.Generic;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Query.Infrastructure;

namespace EntityFrameworkCore.Paging.Query.Filtering;

internal sealed class SeekOrderCollectionBuilder
{
    private readonly EvaluateMemberVisitor _evaluateMember;

    public SeekOrderCollectionBuilder(EvaluateMemberVisitor evaluateMember)
    {
        _evaluateMember = evaluateMember;
    }

    public SeekOrderCollection Build(ConstantExpression origin, Expression query)
    {
        var parameter = Expression
            .Parameter(
                origin.Type,
                origin.Type.Name[0].ToString().ToLowerInvariant());

        var replaceParameter = new ReplaceParameterVisitor(parameter);
        var findOrderProperties = new FindOrderPropertiesVisitor(replaceParameter, _evaluateMember);

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
