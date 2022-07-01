using System;
using System.Linq.Expressions;
using System.Reflection;

using MtgViewer.Utils;

namespace MtgViewer.Services.Search;

internal class PredicateVisitor : ExpressionVisitor
{
    private readonly ConstantExpression _search;
    private readonly MethodInfo _addParameterMethod;

    public PredicateVisitor(MtgCardSearch search, MethodInfo addParameterMethod)
    {
        _search = Expression.Constant(search);
        _addParameterMethod = addParameterMethod;
    }

    protected override Expression VisitLambda<T>(Expression<T> node)
        => Visit(node.Body);

    protected override Expression VisitMember(MemberExpression node)
    {
        return (Visit(node.Expression), node.Member) switch
        {
            (ParameterExpression p, _) when p.Type == typeof(CardQuery)
                => Expression.Constant(node.Member.Name),

            (ConstantExpression { Value: object o }, PropertyInfo info)
                => Expression.Constant(info.GetValue(o), typeof(object)),

            (ConstantExpression { Value: object o }, FieldInfo info)
                => Expression.Constant(info.GetValue(o), typeof(object)),

            _ => node
        };
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType is ExpressionType.Convert)
        {
            return Visit(node.Operand);
        }

        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
        => Expression.Constant(node.Value, typeof(object));

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType is ExpressionType.Coalesce)
        {
            object? eval = Expression
                .Lambda(node)
                .Compile()
                .DynamicInvoke();

            return Expression.Constant(eval, typeof(object));
        }

        if (node.NodeType is not ExpressionType.Equal)
        {
            return node;
        }

        var left = Visit(node.Left);
        var right = Visit(node.Right);

        // only the property name will have a string type

        if (left.Type == typeof(string))
        {
            return AddParameter(left, right);
        }

        if (right.Type == typeof(string))
        {
            return AddParameter(right, left);
        }

        return node;
    }

    private Expression<Func<IMtgCardSearch>> AddParameter(Expression propertyName, Expression value)
        => Expression.Lambda<Func<IMtgCardSearch>>(
            Expression.Call(
                instance: _search,
                method: _addParameterMethod,
                arg0: propertyName,
                arg1: value));

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method == TypeHelpers.StringContains
            && Visit(node.Object) is Expression containsName)
        {
            return AddParameter(containsName, Visit(node.Arguments[0]));
        }

        if (node.Method == TypeHelpers.EnumerableAll.MakeGenericMethod(typeof(string)))
        {
            var allName = AllPredicateVisitor.Instance.Visit(node.Arguments[1]);

            return AddParameter(allName, Visit(node.Arguments[0]));
        }

        return node;
    }

    private sealed class AllPredicateVisitor : ExpressionVisitor
    {
        public static AllPredicateVisitor Instance { get; } = new();

        protected override Expression VisitLambda<T>(Expression<T> node)
            => Visit(node.Body);

        protected override Expression VisitMember(MemberExpression node)
        {
            if (Visit(node.Expression) is ParameterExpression p && p.Type == typeof(CardQuery))
            {
                return Expression.Constant(node.Member.Name);
            };

            return node;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType is ExpressionType.Convert)
            {
                return Visit(node.Operand);
            }

            return node;
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Type == typeof(string))
            {
                return Expression.Constant(node.Value, typeof(object));
            }

            return node;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node.Type == typeof(string))
            {
                return Expression.Parameter(typeof(object), node.Name);
            }

            return node;
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is not ExpressionType.Equal)
            {
                return node;
            }

            var left = Visit(node.Left);
            var right = Visit(node.Right);

            // only the property name will have a string type

            if (left.Type == typeof(string))
            {
                return left;
            }

            if (right.Type == typeof(string))
            {
                return right;
            }

            return node;
        }
    }
}
