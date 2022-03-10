using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

internal class FindSelect<TSource, TResult> : ExpressionVisitor
{
    private static FindSelect<TSource, TResult>? _instance;
    private static MethodInfo? _selectMethod;

    public static ExpressionVisitor Instance => _instance ??= new();


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


internal class RemoveSelect<TSource, TResult> : ExpressionVisitor
{
    private static RemoveSelect<TSource, TResult>? _instance;
    private static MethodInfo? _selectMethod;

    public static ExpressionVisitor Instance => _instance ??= new();


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


internal record SelectResult<TSource, TResult>(
    IQueryable<TSource> Query,
    Expression<Func<TSource, TResult>> Selector);


internal static class SelectQueries
{
    internal static SelectResult<TSource, TResult> GetSelectQuery<TSource, TResult>(IQueryable<TResult> source)
    {
        var removeSelect = RemoveSelect<TSource, TResult>.Instance.Visit(source.Expression);

        var query = source.Provider.CreateQuery<TSource>(removeSelect);

        var selector = FindSelect<TSource, TResult>.Instance.Visit(source.Expression)
            as Expression<Func<TSource, TResult>>;

        if (selector is null)
        {
            throw new InvalidOperationException(
                $"Select from {typeof(TSource).Name} to {typeof(TResult).Name} could not be found");
        }

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


internal class InsertTakeVisitor<TEntity> : ExpressionVisitor
{
    private readonly int _count;
    private InsertReverseTakeVisitor? _reverseTake;

    public InsertTakeVisitor(int count)
    {
        _count = count;
    }

    [return: NotNullIfNotNull("node")]
    public override Expression? Visit(Expression? node)
    {
        if (node is QueryRootExpression root
            && root.EntityType.ClrType == typeof(TEntity))
        {
            return Expression.Call(
                null,
                QueryableMethods.Take.MakeGenericMethod(typeof(TEntity)),
                root,
                Expression.Constant(_count));
        }

        return base.Visit(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        bool validMethodCall = IsValidMethod(node);

        if (!validMethodCall && node.Arguments.ElementAtOrDefault(0)
            is Expression parent)
        {
            var visitedParent = base.Visit(parent);

            return node.Update(
                node.Object,
                node.Arguments
                    .Skip(1)
                    .Prepend(visitedParent));
        }

        if (!validMethodCall)
        {
            return node;
        }

        _reverseTake ??= new(_count);

        if (_reverseTake.Visit(node) is MethodCallExpression inserted)
        {
            return inserted;
        }

        return Expression.Call(
            null,
            QueryableMethods.Take.MakeGenericMethod(typeof(TEntity)),
            node,
            Expression.Constant(_count));
    }


    private bool IsValidMethod(MethodCallExpression node)
    {
        var generics = node.Method.GetGenericArguments();

        bool correctType = generics.ElementAtOrDefault(0) == typeof(TEntity);

        return correctType && generics.Length == 1
            || correctType && ExpressionHelpers.IsOrderedMethod(node);
    }
    

    private class InsertReverseTakeVisitor : ExpressionVisitor
    {
        private readonly int _count;
        public InsertReverseTakeVisitor(int count)
        {
            _count = count;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
            {
                return ExpressionConstants.Null;
            }

            if (node.Method == QueryableMethods.Reverse.MakeGenericMethod(typeof(TEntity))
                && FindSecondReverseVisitor.Instance.Visit(node) is ConstantExpression second
                && second.Value is null)
            {
                var take = Expression.Call(
                    null,
                    QueryableMethods.Take.MakeGenericMethod(typeof(TEntity)),
                    parent,
                    Expression.Constant(_count));

                return node.Update(
                    node.Object,
                    node.Arguments
                        .Skip(1)
                        .Prepend(take));
            }

            return Visit(parent);
        }
    }


    private class FindSecondReverseVisitor : ExpressionVisitor
    {
        private static FindSecondReverseVisitor? _instance;
        public static ExpressionVisitor Instance => _instance ??= new();

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
            {
                return node;
            }

            if (node.Method == QueryableMethods.Reverse.MakeGenericMethod(typeof(TEntity)))
            {
                return ExpressionConstants.Null;
            }

            if (node.Method.IsGenericMethod
                && node.Method.GetGenericArguments()[0] == typeof(TEntity))
            {
                return Visit(parent);
            }

            return node;
        }
    }
}
