using System;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Query.Infrastructure;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class SeekListExpression : Expression
{
    public SeekListExpression(Expression translation, SeekQueryExpression? seek = null)
    {
        ArgumentNullException.ThrowIfNull(translation);

        var elementType = ExpressionHelpers.FindElementType(translation)
            ?? throw new ArgumentException("Source expression must be a query", nameof(translation));

        Type = typeof(SeekList<>).MakeGenericType(elementType);
        Translation = translation;
        Seek = seek ?? new SeekQueryExpression(elementType);
    }

    public override ExpressionType NodeType => ExpressionType.Extension;

    public override Type Type { get; }

    public Expression Translation { get; }

    public SeekQueryExpression Seek { get; }

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var visitedTranslation = visitor.Visit(Translation);
        var visitedSeek = visitor.Visit(Seek);

        if (visitedSeek is not null and not SeekQueryExpression)
        {
            throw new InvalidOperationException($"{nameof(Seek)} is invalid type: {visitedSeek.Type.Name}");
        }

        return Update(visitedTranslation, visitedSeek as SeekQueryExpression);
    }

    public SeekListExpression Update(Expression translation, SeekQueryExpression? seek)
    {
        if (ExpressionEqualityComparer.Instance.Equals(translation, Translation)
            && seek is not null
            && seek.Equals(Seek))
        {
            return this;
        }

        return new SeekListExpression(translation, seek);
    }
}
