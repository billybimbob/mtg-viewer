using System.Linq.Expressions;
using System.Reflection;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class EvaluateMemberVisitor : ExpressionVisitor
{
    protected override Expression VisitMember(MemberExpression node)
    {
        if (Visit(node.Expression) is not ConstantExpression source)
        {
            return node;
        }

        if (node.Member is PropertyInfo prop)
        {
            object? evaluatedMember = prop.GetValue(source.Value);
            return Expression.Constant(evaluatedMember);
        }

        if (node.Member is FieldInfo field)
        {
            object? evaluatedMember = field.GetValue(source.Value);
            return Expression.Constant(evaluatedMember);
        }

        return node;
    }
}
