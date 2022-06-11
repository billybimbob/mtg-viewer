using System;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query;

internal class SeekExpression : Expression
{
    internal SeekExpression(Expression query, ConstantExpression origin, SeekDirection direction, int? take)
    {
        Query = query;
        Origin = origin;
        Direction = direction;
        Take = take;
    }

    public SeekExpression(IQueryable query, object? origin, SeekDirection direction, int? take)
        : this(query.Expression, Constant(origin), direction, take)
    {
        // key is already boxed in the expression constant call, so boxing cannot be avoided
    }

    public Expression Query { get; }

    public ConstantExpression Origin { get; }

    public SeekDirection Direction { get; }

    public int? Take { get; }

    public override Type Type => Query.Type;

    public override ExpressionType NodeType => ExpressionType.Extension;

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var newQuery = visitor.Visit(Query);
        var newOrigin = visitor.Visit(Origin);

        bool hasChanges = newQuery != Query || newOrigin != Origin;

        return (newOrigin, hasChanges) switch
        {
            (ConstantExpression o, true) => new SeekExpression(newQuery, o, Direction, Take),
            (_, true) => new SeekExpression(newQuery, Origin, Direction, Take),
            _ => this
        };
    }

    public SeekExpression Update(object? origin, SeekDirection direction, int? take)
    {
        if (Origin.Value == origin && Direction == direction && Take == take)
        {
            return this;
        }

        return new SeekExpression(Query, Constant(origin), direction, take);
    }
}
