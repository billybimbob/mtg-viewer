using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class SeekOrderCollection
{
    private readonly OriginTranslator _origin;
    private readonly IReadOnlyList<KeyOrder> _orderKeys;
    private readonly IReadOnlyDictionary<MemberExpression, NullOrder> _nullOrders;

    public ParameterExpression Parameter { get; }

    private SeekOrderCollection(
        OriginTranslator origin,
        IReadOnlyList<KeyOrder> orderKeys,
        IReadOnlyDictionary<MemberExpression, NullOrder> nullOrders,
        ParameterExpression parameter)
    {
        _origin = origin;
        _orderKeys = orderKeys;
        _nullOrders = nullOrders;

        Parameter = parameter;
    }

    public static SeekOrderCollection Build(ConstantExpression origin, Expression query)
    {
        var parameter = Expression
            .Parameter(
                origin.Type,
                origin.Type.Name[0].ToString().ToLowerInvariant());

        var findOrderProperties = new FindOrderPropertiesVisitor(parameter);
        var findNullProperties = new FindNullPropertiesVisitor(parameter);

        var orderKeys = findOrderProperties.ScanProperties(query);
        var nullOrders = findNullProperties.ScanProperties(query);

        var targetTranslations = orderKeys
            .Select(k => k.Key)
            .OfType<MemberExpression>();

        var originTranslator = OriginTranslator.Build(origin, targetTranslations, nullOrders.Keys);

        return new SeekOrderCollection(originTranslator, orderKeys, nullOrders, parameter);
    }

    public IReadOnlyList<FilterProperty> BuildFilterProperties()
    {
        if (_orderKeys.Count is 0)
        {
            return Array.Empty<FilterProperty>();
        }

        FilterProperty? previousProperty = null;

        var filterProperties = new List<FilterProperty>(_orderKeys.Count);

        foreach (var key in _orderKeys)
        {
            var currentProperty = new FilterProperty(key, previousProperty);

            filterProperties.Add(currentProperty);
            previousProperty = currentProperty;
        }

        return filterProperties;
    }

    public MemberExpression? Translate(MemberExpression node)
        => _origin.Translate(node);

    public bool IsCallerNull(MemberExpression node)
        => _origin.IsCallerNull(node);

    public NullOrder GetNullOrder(MemberExpression node)
        => _nullOrders.GetValueOrDefault(node);
}
