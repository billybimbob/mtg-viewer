using System.Collections.Generic;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class SeekOrderCollection
{
    public ParameterExpression Parameter { get; }
    public IReadOnlyList<LinkedOrderProperty> OrderProperties { get; }

    public SeekOrderCollection(ParameterExpression parameter, IReadOnlyList<LinkedOrderProperty> orderProperties)
    {
        Parameter = parameter;
        OrderProperties = orderProperties;
    }
}
