using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

internal class FindSelect : ExpressionVisitor
{
    private readonly Type _source;
    private readonly Type _result;

    public FindSelect(Type source, Type result)
    {
        _source = source;
        _result = result;
    }

    private MethodInfo? _selectMethod;


    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (_source == _result)
        {
            return node;
        }

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
}


internal class RemoveSelect : ExpressionVisitor
{
    private readonly Type _source;
    private readonly Type _result;

    public RemoveSelect(Type source, Type result)
    {
        _source = source;
        _result = result;
    }

    private MethodInfo? _selectMethod;

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (_source == _result)
        {
            return node;
        }

        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        _selectMethod ??= QueryableMethods.Select.MakeGenericMethod(_source, _result);

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
        var removeSelect = new RemoveSelect(typeof(TSource), typeof(TResult))
            .Visit(source.Expression);

        var query = source.Provider.CreateQuery<TSource>(removeSelect);

        var selector = new FindSelect(typeof(TSource), typeof(TResult))
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