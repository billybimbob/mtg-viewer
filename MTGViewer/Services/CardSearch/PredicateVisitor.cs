using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace MTGViewer.Services;

internal class PredicateVisitor : ExpressionVisitor
{
    private Expression _parent;
    private ParameterExpression? _parameter;
    private Dictionary<string, Expression> _propertyNames;

    public PredicateVisitor(MtgApiQuery parent)
    {
        _parent = Expression.Constant(parent);
        _propertyNames = new();
    }


    private static UnaryExpression? _nullDictionary;
    private static UnaryExpression NullDictionary =>
        _nullDictionary ??=
            Expression.Convert(
                ExpressionConstants.Null,
                typeof(IDictionary<,>).MakeGenericType(typeof(string), typeof(object)));



    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (node.Type != typeof(CardQuery))
        {
            return Expression.Empty();
        }

        return base.VisitParameter(node);
    }


    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        if (Visit(node.Parameters.ElementAtOrDefault(0)) is ParameterExpression p)
        {
            _parameter = p;
        }

        return Visit(node.Body);
    }


    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Type == typeof(object))
        {
            return node;
        }

        return Expression.Constant(node.Value, typeof(object));
    }


    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.Operand is not LambdaExpression lambda)
        {
            var operand = Visit(node.Operand);

            if (operand is DefaultExpression
                || operand is ConstantExpression && operand.Type == typeof(string))
            {
                return operand;
            }

            lambda = Expression.Lambda(node.Operand);
        }

        var value = lambda
            .Compile()
            .DynamicInvoke();

        return Expression.Constant(value, typeof(object));
    }


    protected override Expression VisitDefault(DefaultExpression node)
    {
        return ExpressionConstants.Null;
    }


    protected override Expression VisitMember(MemberExpression node)
    {
        return Visit(node.Expression) switch
        {
            ParameterExpression p when p == _parameter => GetOrCreateProperty(node),

            ConstantExpression c when c.Type == typeof(string) =>
                Expression.Constant($"{c.Value}.{node.Member.Name}"),

            ConstantExpression
                or UnaryExpression => Expression.Quote(Expression.Lambda(node)),

            DefaultExpression d => d,

            _ => node
        };
    }


    private Expression GetOrCreateProperty(MemberExpression property)
    {
        string propertyName = property.Member.Name;

        if (_propertyNames.TryGetValue(propertyName, out var name))
        {
            return name;
        }

        return _propertyNames[propertyName] = Expression.Constant(propertyName);
    }


    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType is ExpressionType.Coalesce)
        {
            return Expression.Quote(Expression.Lambda(node));
        }

        if (node.NodeType is not ExpressionType.Equal)
        {
            return node;
        }

        var left = Visit(node.Left);
        var right = Visit(node.Right);

        // only the card query will have a non-object constant

        if (left is ConstantExpression lProperty && lProperty.Type == typeof(string))
        {
            return CallQuery(lProperty, right);
        }

        if (right is ConstantExpression rProperty && rProperty.Type == typeof(string))
        {
            return CallQuery(rProperty, left);
        }

        return node;
    }
    

    private MethodCallExpression CallQuery(ConstantExpression propertyName, Expression value)
    {
        return Expression.Call(
            _parent,
            MtgApiQuery.QueryMethod,
            NullDictionary, propertyName, Visit(value));
    }


    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method == ExpressionConstants.StringContains
            && Visit(node.Object) is ConstantExpression propertyName
            && propertyName.Type == typeof(string))
        {
            return CallQuery(propertyName, Visit(node.Arguments[0]));
        }

        if (node.Method == ExpressionConstants.GetEnumerableAny<string>()
            && Visit(node.Arguments[1]) is MethodCallExpression innerQuery
            && innerQuery.Method == MtgApiQuery.QueryMethod
            && innerQuery.Arguments[1] is ConstantExpression innerProperty
            && innerProperty.Type == typeof(string))
        {
            return CallQuery(innerProperty, Visit(node.Arguments[0]));
        }

        var caller = Visit(node.Object);
        var args = Visit(node.Arguments);

        if (!args.OfType<DefaultExpression>().Any())
        {
            caller = caller switch
            {
                ConstantExpression c
                    when node.Object is not null && c.Type != node.Object.Type =>
                    Expression.Constant(c.Value, node.Object.Type),

                null or UnaryExpression or _ => caller,
            };

            return Expression.Quote(
                Expression.Lambda(
                    Expression.Call(caller, node.Method, args)));
        }

        return node;
    }
}