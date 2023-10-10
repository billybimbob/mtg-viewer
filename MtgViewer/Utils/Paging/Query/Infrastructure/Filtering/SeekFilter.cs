using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class SeekFilter
{
    private readonly SeekOrderCollectionBuilder _seekCollectionBuilder;

    public SeekFilter(EvaluateMemberVisitor evaluateMember)
    {
        _seekCollectionBuilder = new SeekOrderCollectionBuilder(evaluateMember);
    }

    public LambdaExpression? CreateFilter(Expression query, SeekDirection direction, ConstantExpression origin)
    {
        if (origin.Value is null)
        {
            return null;
        }

        var originTranslatorBuilder = new OriginTranslatorBuilder(origin);

        var orderCollection = _seekCollectionBuilder.Build(origin, query);
        var originTranslator = originTranslatorBuilder.Build(orderCollection);

        var builder = new SeekFilterExpressionBuilder(orderCollection, originTranslator, direction);

        return builder.Build();
    }

}
