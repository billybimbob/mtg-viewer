using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal class FindRootVisitor : ExpressionVisitor
{
    private static FindRootVisitor? _instance;
    public static FindRootVisitor Instance => _instance ??= new();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        return Visit(parent);
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is QueryRootExpression root)
        {
            return root;
        }

        if (node is SeekExpression seek)
        {
            return Visit(seek.Query);
        }

        return base.VisitExtension(node);
    }
}
