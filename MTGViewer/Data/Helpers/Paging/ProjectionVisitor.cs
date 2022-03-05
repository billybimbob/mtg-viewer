using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

internal class FindSelect<TSource, TResult> : ExpressionVisitor
{
    private static MethodInfo? _selectMethod;
    private static FindSelect<TSource, TResult>? _instance;

    public static ExpressionVisitor Instance => _instance ??= new();


    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (typeof(TSource) == typeof(TResult))
        {
            return node;
        }

        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        _selectMethod ??= QueryableMethods.Select
            .MakeGenericMethod(typeof(TSource), typeof(TResult));

        if (node.Method == _selectMethod
            && node.Arguments.ElementAtOrDefault(1) is UnaryExpression unary
            && unary.NodeType is ExpressionType.Quote
            && unary.Operand is Expression<Func<TSource, TResult>> selector)
        {
            return selector;
        }

        return Visit(parent);
    }
}


internal class RemoveSelect<TSource, TResult> : ExpressionVisitor
{
    private static MethodInfo? _selectMethod;
    private static RemoveSelect<TSource, TResult>? _instance;

    public static ExpressionVisitor Instance => _instance ??= new();


    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (typeof(TSource) == typeof(TResult))
        {
            return node;
        }

        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        _selectMethod ??= QueryableMethods.Select
            .MakeGenericMethod(typeof(TSource), typeof(TResult));

        var method = node.Method;
        if (method.IsGenericMethod && method == _selectMethod)
        {
            return parent;
        }

        return Visit(parent);
    }
}


internal static class SelectQueries
{
    internal readonly record struct SelectQuery<TSource, TResult>(
        IQueryable<TSource>? Query,
        Expression<Func<TSource, TResult>>? Selector);


    internal static SelectQuery<TSource, TResult> GetSelectQuery<TSource, TResult>(IQueryable<TResult> source)
    {
        var removeSelect = RemoveSelect<TSource, TResult>.Instance
            .Visit(source.Expression);

        var query = source.Provider.CreateQuery<TSource>(removeSelect);

        var selector = FindSelect<TSource, TResult>.Instance
            .Visit(source.Expression) as Expression<Func<TSource, TResult>>;

        return new (query, selector);
    }
}


internal class FindRootQuery : ExpressionVisitor
{
    private static FindRootQuery? _instance;
    public static ExpressionVisitor Instance => _instance ??= new();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        if (parent is QueryRootExpression root)
        {
            return root;
        }

        return Visit(parent);
    }
}