using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Query.Infrastructure;

namespace EntityFrameworkCore.Paging.Query.Filtering;

internal sealed class SeekFilter
{
    private readonly SeekOrderCollectionBuilder _seekCollectionBuilder;

    public SeekFilter(EvaluateMemberVisitor evaluateMember)
    {
        _seekCollectionBuilder = new SeekOrderCollectionBuilder(evaluateMember);
    }

    public LambdaExpression? CreateFilter(Expression query, SeekDirection? direction, ConstantExpression? origin)
    {
        if (origin?.Value is null)
        {
            return null;
        }

        if (direction is not SeekDirection dir)
        {
            return null;
        }

        var originTranslatorBuilder = new OriginTranslatorBuilder(origin);

        var orderCollection = _seekCollectionBuilder.Build(origin, query);
        var originTranslator = originTranslatorBuilder.Build(orderCollection);

        var builder = new SeekFilterExpressionBuilder(orderCollection, originTranslator, dir);

        return builder.Build();
    }

}
