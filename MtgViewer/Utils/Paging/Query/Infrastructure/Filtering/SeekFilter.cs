using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class SeekFilter
{
    private readonly EvaluateMemberVisitor _evaluateMember;

    public SeekFilter(EvaluateMemberVisitor evaluateMember)
    {
        _evaluateMember = evaluateMember;
    }

    public LambdaExpression? CreateFilter(Expression query, SeekDirection? direction, ConstantExpression? origin)
    {
        if (direction is not SeekDirection dir)
        {
            return null;
        }

        if (origin is null)
        {
            return null;
        }

        var orderCollection = SeekOrderCollection.Build(_evaluateMember, origin, query);
        var originTranslator = OriginTranslator.Build(origin, orderCollection.OrderProperties);

        var builder = new SeekFilterBuilder(orderCollection, originTranslator, dir);

        return builder.Build();
    }

}
