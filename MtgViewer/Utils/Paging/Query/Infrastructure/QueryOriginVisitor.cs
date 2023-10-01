using System;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

using EntityFrameworkCore.Paging.Utils;
using System.Reflection;
using System.Collections.Generic;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal class QueryOriginVisitor : ExpressionVisitor
{
    private readonly IQueryProvider _provider;
    private readonly AfterVisitor _afterParser;
    private readonly OriginIncludesVisitor _originIncludes;

    public QueryOriginVisitor(IQueryProvider provider)
    {
        _provider = provider;
        _afterParser = new AfterVisitor();
        _originIncludes = new OriginIncludesVisitor();
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsSeekBy(node))
        {
            return node.Arguments[0];
        }

        if (Visit(node.Arguments.ElementAtOrDefault(0)) is not Expression parent)
        {
            return node;
        }

        if (!ExpressionHelpers.IsAfter(node))
        {
            return parent;
        }

        return _afterParser.Visit(node.Arguments[1]) switch
        {
            ConstantExpression origin => origin,
            LambdaExpression predicate => BuildOriginQuery(parent, predicate).Expression,
            _ => node
        };
    }

    private IQueryable BuildOriginQuery(Expression parent, LambdaExpression predicate)
    {
        var query = _provider
            .CreateQuery(parent)
            .Where(predicate);

        var findSelector = new FindSelectVisitor(query.ElementType);

        if (findSelector.Visit(query.Expression) is LambdaExpression)
        {
            return query;
        }

        foreach (string include in ScanIncludes(query.Expression))
        {
            query = query.Include(include);
        }

        return query.AsNoTracking();
    }

    private IEnumerable<string> ScanIncludes(Expression expression)
    {
        if (_originIncludes.Visit(expression) is not ConstantExpression { Value: IEnumerable<string> includes })
        {
            includes = Array.Empty<string>();
        }

        return includes;
    }

    #region After Translation

    private sealed class AfterVisitor : ExpressionVisitor
    {
        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType is ExpressionType.Quote)
            {
                return Visit(node.Operand);
            }

            return node;
        }

        protected override Expression VisitLambda<TFunc>(Expression<TFunc> node)
        {
            var body = Visit(node.Body);

            if (ExpressionHelpers.IsNull(body))
            {
                return body;
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

            if (ExpressionHelpers.IsNull(left) && right is ParameterExpression)
            {
                return left;
            }

            if (left is ParameterExpression && ExpressionHelpers.IsNull(right))
            {
                return right;
            }

            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var source = Visit(node.Expression);

            if (source is ParameterExpression)
            {
                return source;
            }

            if (source is not ConstantExpression constantSource)
            {
                return node;
            }

            if (node.Member is not PropertyInfo prop)
            {
                return node;
            }

            object? evaluatedMember = prop.GetValue(constantSource.Value);

            return Expression.Constant(evaluatedMember);
        }
    }

    #endregion

    private sealed class FindSelectVisitor : ExpressionVisitor
    {
        private readonly Type _resultType;

        public FindSelectVisitor(Type resultType)
        {
            _resultType = resultType;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
            {
                return node;
            }

            if (node.Method.IsGenericMethod is false)
            {
                return Visit(parent);
            }

            var method = node.Method.GetGenericMethodDefinition();

            if (method == QueryableMethods.Select
                && node.Method.GetGenericArguments()[1] == _resultType)
            {
                return Visit(node.Arguments[1]);
            }

            if (method == QueryableMethods.SelectManyWithoutCollectionSelector
                && node.Method.GetGenericArguments()[1] == _resultType)
            {
                return Visit(node.Arguments[1]);
            }

            // TODO: account for SelectWithCollectionSelector

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
}
