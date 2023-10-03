using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class SeekFilter
{
    private readonly Expression _query;
    private readonly ConstantExpression? _origin;
    private readonly SeekDirection? _direction;

    public SeekFilter(Expression query, ConstantExpression? origin, SeekDirection? direction)
    {
        _query = query;
        _origin = origin;
        _direction = direction;
    }

    public LambdaExpression? CreateFilter()
    {
        if (_origin is null)
        {
            return null;
        }

        if (_direction is not SeekDirection dir)
        {
            return null;
        }

        var orderCollection = SeekOrderCollection.Build(_origin, _query);
        var originTranslator = OriginTranslator.Build(_origin, orderCollection.OrderProperties);

        var builder = new SeekFilterBuilder(orderCollection, originTranslator, dir);
        return builder.Build();
    }

}
