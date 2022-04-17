using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

internal record SelectResult<TSource, TResult>(
    IQueryable<TSource> Query,
    Expression<Func<TSource, TResult>> Selector);

internal record SelectResult(IQueryable Query, LambdaExpression Selector);

internal static class SelectQueries
{
    internal static SelectResult<TSource, TResult> GetSelectQuery<TSource, TResult>(IQueryable<TResult> source)
    {
        var selector = FindSelect<TSource, TResult>.Instance.Visit(source.Expression) switch
        {
            Expression<Func<TSource, TResult>> e => e,

            _ => throw new InvalidOperationException(
                $"Select from {typeof(TSource).Name} to {typeof(TResult).Name} could not be found")
        };

        var removeSelect = RemoveSelect<TSource, TResult>.Instance.Visit(source.Expression);

        var query = source.Provider.CreateQuery<TSource>(removeSelect);

        return new SelectResult<TSource, TResult>(query, selector);
    }

    internal static SelectResult GetSelectQuery<TResult>(Type source, IQueryable<TResult> results)
    {
        var findSelect = new FindSelect(source, results.ElementType);

        var selector = findSelect.Visit(results.Expression) switch
        {
            LambdaExpression l
                and { Parameters.Count: 1 }
                when l.Body.Type == results.ElementType => l,

            _ => throw new InvalidOperationException(
                $"Select from {source.Name} to {results.ElementType.Name} could not be found")
        };

        var removeSelect = new RemoveSelect(source, results.ElementType);

        var query = results.Provider
            .CreateQuery(removeSelect
                .Visit(results.Expression));

        return new SelectResult(query, selector);
    }
}

internal class FindSelect<TSource, TResult> : ExpressionVisitor
{
    private static FindSelect<TSource, TResult>? s_instance;
    private static MethodInfo? _selectMethod;

    public static ExpressionVisitor Instance => s_instance ??= new();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        _selectMethod ??= QueryableMethods.Select.MakeGenericMethod(typeof(TSource), typeof(TResult));

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

internal class FindSelect : ExpressionVisitor
{
    private readonly Type _source;
    private readonly Type _result;
    private MethodInfo? _selectMethod;

    public FindSelect(Type source, Type result)
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
}

internal class RemoveSelect<TSource, TResult> : ExpressionVisitor
{
    private static RemoveSelect<TSource, TResult>? s_instance;
    private static MethodInfo? _selectMethod;

    public static ExpressionVisitor Instance => s_instance ??= new();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        _selectMethod ??= QueryableMethods.Select.MakeGenericMethod(typeof(TSource), typeof(TResult));

        if (node.Method == _selectMethod)
        {
            return parent;
        }

        return Visit(parent);
    }
}

internal class RemoveSelect : ExpressionVisitor
{
    private readonly Type _source;
    private readonly Type _result;
    private MethodInfo? _selectMethod;

    public RemoveSelect(Type source, Type result)
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

        if (node.Method == _selectMethod)
        {
            return parent;
        }

        return Visit(parent);
    }
}
