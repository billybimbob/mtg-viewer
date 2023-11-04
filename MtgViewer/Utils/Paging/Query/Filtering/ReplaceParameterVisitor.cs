using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Filtering;

internal sealed class ReplaceParameterVisitor : ExpressionVisitor
{
    private readonly ParameterExpression _parameter;

    public ReplaceParameterVisitor(ParameterExpression parameter)
    {
        _parameter = parameter;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (node.Type == _parameter.Type)
        {
            return _parameter;
        }

        return node;
    }
}
