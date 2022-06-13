using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal class FindSelectVisitor : ExpressionVisitor
{
    private readonly Type _source;
    private readonly Type _result;
    private MethodInfo? _selectMethod;

    public FindSelectVisitor(Type source, Type result)
    {
        _source = source;
        _result = result;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        _selectMethod ??= QueryableMethods.Select.MakeGenericMethod(_source, _result);

        if (node.Method == _selectMethod && node.Arguments.Count == 2)
        {
            return Visit(node.Arguments[1]);
        }

        return Visit(parent);
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType is ExpressionType.Quote)
        {
            return node.Operand;
        }

        return node;
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is SeekExpression seek)
        {
            return Visit(seek.Query);
        }

        return node;
    }
}
