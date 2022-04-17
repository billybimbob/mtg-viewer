using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

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
        if (node is QueryRootExpression { EntityType.ClrType: Type t }
            && t == typeof(TEntity))
        {
            return Expression.Call(
                null,
                QueryableMethods.Take.MakeGenericMethod(typeof(TEntity)),
                node,
                Expression.Constant(_count));
        }

        return base.Visit(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        bool validMethodCall = IsValidMethod(node) || IsSelectMethod(node);

        if (!validMethodCall
            && node.Arguments.ElementAtOrDefault(0) is Expression parent)
        {
            var visitedParent = Visit(parent);

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

        _reverseTake ??= new InsertReverseTakeVisitor(_count);

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

    private static bool IsValidMethod(MethodCallExpression node)
    {
        var generics = node.Method.GetGenericArguments();

        bool correctType = generics.ElementAtOrDefault(0) == typeof(TEntity);

        return correctType && generics.Length == 1
            || correctType && ExpressionHelpers.IsOrderedMethod(node);
    }

    private static bool IsSelectMethod(MethodCallExpression node)
    {
        var generics = node.Method.GetGenericArguments();

        return node is { Method.IsGenericMethod: true }
            && node.Method.GetGenericMethodDefinition() == QueryableMethods.Select
            && generics.ElementAtOrDefault(1) == typeof(TEntity);
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
                && FindSecondReverseVisitor.Instance.Visit(node)
                    is ConstantExpression { Value: null })
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
        private static FindSecondReverseVisitor? s_instance;
        public static ExpressionVisitor Instance => s_instance ??= new();

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
