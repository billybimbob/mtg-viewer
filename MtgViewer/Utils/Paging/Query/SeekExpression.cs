using System;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal class SeekExpression : Expression
{
    internal SeekExpression(QueryRootExpression root, ConstantExpression origin, SeekDirection direction, int? take)
    {
        Root = root;
        Origin = origin;
        Direction = direction;
        Take = take;
    }

    public SeekExpression(IQueryable query, object? origin, SeekDirection direction, int? take)
    {
        if (FindRootQuery.Instance.Visit(query.Expression) is not QueryRootExpression root)
        {
            throw new ArgumentException($"Cannot find {nameof(QueryRootExpression)}", nameof(query));
        }

        // key is already boxed in the expression constant call, so boxing cannot be avoided

        Root = root;
        Origin = Constant(origin);
        Direction = direction;
        Take = take;
    }

    public QueryRootExpression Root { get; }

    public ConstantExpression Origin { get; }

    public SeekDirection Direction { get; }

    public int? Take { get; }

    public override Type Type => Root.Type;

    public override ExpressionType NodeType => ExpressionType.Extension;

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var newRoot = visitor.Visit(Root);
        var newOrigin = visitor.Visit(Origin);

        bool hasChanges = newRoot != Root || newOrigin != Origin;

        return (newRoot, newOrigin, hasChanges) switch
        {
            (QueryRootExpression r, ConstantExpression o, true) => new SeekExpression(r, o, Direction, Take),
            (QueryRootExpression r, _, true) => new SeekExpression(r, Origin, Direction, Take),
            (_, ConstantExpression o, true) => new SeekExpression(Root, o, Direction, Take),
            _ => this
        };
    }

    public SeekExpression Update(object? origin, SeekDirection direction, int? take)
    {
        if (Origin.Value == origin && Direction == direction && Take == take)
        {
            return this;
        }

        return new SeekExpression(Root, Constant(origin), direction, take);
    }
}
