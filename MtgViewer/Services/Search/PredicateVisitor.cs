using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

using MtgViewer.Utils;

namespace MtgViewer.Services.Search;

internal class PredicateVisitor : ExpressionVisitor
{
    private static PredicateVisitor? _instance;
    public static PredicateVisitor Instance => _instance ??= new();

    private static UnaryExpression? _nullDictionary;
    private static UnaryExpression NullDictionary =>
        _nullDictionary ??=
            Expression.Convert(
                Expression.Constant(null),
                typeof(IDictionary<string, IMtgParameter>));

    private readonly Dictionary<string, ConstantExpression> _propertyNames = new();

    private AllPredicateVisitor? _allVisitor;
    private ExpressionVisitor AllPredicate => _allVisitor ??= new(_propertyNames);

    protected override Expression VisitLambda<T>(Expression<T> node)
        => Visit(node.Body);

    protected override Expression VisitMember(MemberExpression node)
    {
        return (Visit(node.Expression), node.Member) switch
        {
            (ParameterExpression, _) => GetOrCreatePropertyName(node),

            (ConstantExpression { Value: object o }, PropertyInfo info) =>
                Expression.Constant(
                    info.GetValue(o), typeof(object)),

            (ConstantExpression { Value: object o }, FieldInfo info) =>
                Expression.Constant(
                    info.GetValue(o), typeof(object)),

            _ => node
        };
    }

    private ConstantExpression GetOrCreatePropertyName(MemberExpression property)
    {
        string propertyName = property.Member.Name;

        if (_propertyNames.TryGetValue(propertyName, out var name))
        {
            return name;
        }

        return _propertyNames[propertyName] = Expression.Constant(propertyName);
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (node.Type == typeof(CardQuery))
        {
            return node;
        }

        return Expression.Constant(null);
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

        // only the card query will have a non-object constant

        if (left.Type == typeof(string))
        {
            return CallQuery(left, right);
        }

        if (right.Type == typeof(string))
        {
            return CallQuery(right, left);
        }

        return node;
    }

    private static MethodCallExpression CallQuery(Expression propertyName, Expression value)
    {
        return Expression.Call(
            null,
            MtgApiQuery.QueryMethod,
            NullDictionary, propertyName, value);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method == TypeHelpers.StringContains
            && Visit(node.Object) is Expression propertyName)
        {
            return CallQuery(propertyName, Visit(node.Arguments[0]));
        }

        if (node.Method == TypeHelpers.EnumerableAll.MakeGenericMethod(typeof(string)))
        {
            var visitPredicate = AllPredicate.Visit(node.Arguments[1]);

            return CallQuery(visitPredicate, Visit(node.Arguments[0]));
        }

        return node;
    }

    private sealed class AllPredicateVisitor : ExpressionVisitor
    {
        private readonly Dictionary<string, ConstantExpression> _propertyNames;

        public AllPredicateVisitor(Dictionary<string, ConstantExpression> propertyNames)
        {
            _propertyNames = propertyNames;
        }

        protected override Expression VisitLambda<T>(Expression<T> node)
            => Visit(node.Body);

        protected override Expression VisitMember(MemberExpression node)
        {
            if (Visit(node.Expression) is ParameterExpression)
            {
                return GetOrCreatePropertyName(node);
            };

            return node;
        }

        private ConstantExpression GetOrCreatePropertyName(MemberExpression property)
        {
            string propertyName = property.Member.Name;

            if (_propertyNames.TryGetValue(propertyName, out var name))
            {
                return name;
            }

            return _propertyNames[propertyName] = Expression.Constant(propertyName);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node.Type == typeof(CardQuery))
            {
                return node;
            }

            return Expression.Constant(null);
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
            if (node.NodeType is not ExpressionType.Equal)
            {
                return node;
            }

            var left = Visit(node.Left);
            var right = Visit(node.Right);

            // only the card query will have a non-object constant

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
