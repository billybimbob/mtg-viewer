using System;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Query.Infrastructure;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class SeekListExpression : Expression
{
    public SeekListExpression(Expression source, SeekQueryExpression parameters)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parameters);

        var elementType = ExpressionHelpers.FindElementType(source)
            ?? throw new ArgumentException("Source expression must be a query", nameof(source));

        Type = typeof(SeekList<>).MakeGenericType(elementType);
        Source = source;
        Parameters = parameters;
    }

    public override ExpressionType NodeType => ExpressionType.Extension;

    public override Type Type { get; }

    public Expression Source { get; }

    public SeekQueryExpression Parameters { get; }

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var visitedSource = visitor.Visit(Source);
        var visitedParameters = visitor.Visit(Parameters);

        if (visitedParameters is not SeekQueryExpression newParameters)
        {
            throw new InvalidOperationException($"{nameof(Parameters)} is invalid type: {visitedParameters.Type.Name}");
        }

        return Update(visitedSource, newParameters);
    }

    public SeekListExpression Update(Expression source, SeekQueryExpression parameters)
    {
        if (ExpressionEqualityComparer.Instance.Equals(source, Source)
            && parameters.Equals(Parameters))
        {
            return this;
        }

        return new SeekListExpression(source, parameters);
    }
}
