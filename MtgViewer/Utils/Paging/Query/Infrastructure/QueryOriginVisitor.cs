using System;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal class QueryOriginVisitor : ExpressionVisitor
{
    private readonly IQueryProvider _provider;
    private readonly ParseAfterVisitor _afterParser;
    private readonly FindIncludesVisitor _includesFinder;

    public QueryOriginVisitor(IQueryProvider provider, EvaluateMemberVisitor evaluateMember)
    {
        _provider = provider;
        _afterParser = new ParseAfterVisitor(evaluateMember);
        _includesFinder = new FindIncludesVisitor();
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsAfter(node))
        {
            return BuildAfterExpression(node);
        }

        if (ExpressionHelpers.IsSeekBy(node))
        {
            return node.Arguments[0];
        }

        if (node.Arguments.ElementAtOrDefault(0) is Expression parent)
        {
            return Visit(parent);
        }

        return node;
    }

    private Expression BuildAfterExpression(MethodCallExpression node)
    {
        var parsedAfter = _afterParser.Visit(node.Arguments[1]);

        if (parsedAfter is ConstantExpression origin)
        {
            return origin;
        }

        if (parsedAfter is LambdaExpression predicate
            && Visit(node.Arguments[0]) is Expression parent)
        {
            return BuildOriginQuery(parent, predicate).Expression;
        }

        return node;
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

        foreach (string include in _includesFinder.Scan(query.Expression))
        {
            query = query.Include(include);
        }

        return query.AsNoTracking();
    }

    #region After Translation

    private sealed class ParseAfterVisitor : ExpressionVisitor
    {
        private readonly EvaluateMemberVisitor _evaluateMember;

        public ParseAfterVisitor(EvaluateMemberVisitor evaluateMember)
        {
            _evaluateMember = evaluateMember;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType is ExpressionType.Quote)
            {
                return Visit(node.Operand);
            }

            if (node.NodeType is ExpressionType.Convert)
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

            if (ExpressionHelpers.IsNull(left) && right is MemberExpression)
            {
                return left;
            }

            if (left is MemberExpression && ExpressionHelpers.IsNull(right))
            {
                return right;
            }

            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
            => _evaluateMember.Visit(node);
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
