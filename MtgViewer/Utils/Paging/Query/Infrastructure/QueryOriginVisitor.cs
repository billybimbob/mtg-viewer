using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal class QueryOriginVisitor : ExpressionVisitor
{
    private readonly IQueryProvider _provider;

    public QueryOriginVisitor(IQueryProvider provider)
    {
        _provider = provider;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments is not [Expression source, ..])
        {
            return node;
        }

        if (ExpressionHelpers.IsSeekBy(node))
        {
            return source;
        }

        if (!ExpressionHelpers.IsAfter(node) || node.Arguments is not [_, Expression after])
        {
            return Visit(source);
        }

        return AfterVisitor.Instance.Visit(after) switch
        {
            ConstantExpression origin => origin,
            LambdaExpression predicate => BuildOriginQuery(Visit(source), predicate).Expression,
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

        var includes = OriginIncludeVisitor.Scan(query.Expression);

        foreach (string include in includes)
        {
            query = query.Include(include);
        }

        return query.AsNoTracking();
    }

    private sealed class FindSelectVisitor : ExpressionVisitor
    {
        private readonly Type _resultType;

        public FindSelectVisitor(Type resultType)
        {
            _resultType = resultType;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments is not [Expression parent, ..])
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

    #region After Translation

    private sealed class AfterVisitor : ExpressionVisitor
    {
        public static AfterVisitor Instance { get; } = new();

        private AfterVisitor()
        {
        }

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
            var left = Visit(node.Left);

            if (ExpressionHelpers.IsNull(left))
            {
                return left;
            }

            var right = Visit(node.Right);

            if (ExpressionHelpers.IsNull(right))
            {
                return right;
            }

            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            // keep eye on, could be slower than just pass thru

            if (MemberEvaluationVisitor.Instance.Visit(node) is not Expression<Func<object?>> eval)
            {
                return node;
            }

            if (eval.Compile().Invoke() is null)
            {
                return Expression.Constant(null, node.Type);
            }

            return node;
        }
    }

    private sealed class MemberEvaluationVisitor : ExpressionVisitor
    {
        public static MemberEvaluationVisitor Instance { get; } = new();

        private MemberEvaluationVisitor()
        {
        }

        [return: NotNullIfNotNull(nameof(node))]
        public override Expression? Visit(Expression? node)
        {
            return base.Visit(node) switch
            {
                MemberExpression m and { Type.IsValueType: true }
                    => Expression.Lambda<Func<object?>>(
                        Expression.Convert(m, typeof(object))),

                MemberExpression m
                    => Expression.Lambda<Func<object?>>(m),

                _ => node
            };
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var source = base.Visit(node.Expression);

            if (source is ParameterExpression)
            {
                return source;
            }

            if (source?.Type == node.Expression?.Type)
            {
                return node.Update(source);
            }

            return node;
        }
    }

    #endregion

    #region Find Include Properties

    private sealed class OriginIncludeVisitor : ExpressionVisitor
    {
        public static IReadOnlyCollection<string> Scan(Expression node)
        {
            var findIncludes = new OriginIncludeVisitor();

            _ = findIncludes.Visit(node);

            return findIncludes._includes;
        }

        private readonly HashSet<string> _includes;
        private FindIncludeVisitor? _findInclude;

        private OriginIncludeVisitor()
        {
            _includes = new HashSet<string>();
        }

        [return: NotNullIfNotNull(nameof(node))]
        public new Expression? Visit(Expression? node)
        {
            _includes.Clear();

            return base.Visit(node);
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is EntityQueryRootExpression root)
            {
                _findInclude = new FindIncludeVisitor(root.EntityType);
            }

            return base.VisitExtension(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (_findInclude?.Visit(node) is ConstantExpression { Value: string includeChain })
            {
                const StringComparison ordinal = StringComparison.Ordinal;

                _includes.RemoveWhere(s => includeChain.StartsWith(s, ordinal));
                _includes.Add(includeChain);
            }

            return base.VisitMethodCall(node);
        }
    }

    private sealed class FindIncludeVisitor : ExpressionVisitor
    {
        private readonly IReadOnlyEntityType _entity;

        public FindIncludeVisitor(IReadOnlyEntityType entity)
        {
            _entity = entity;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (ExpressionHelpers.IsOrderedMethod(node)
                && node.Method.GetGenericArguments().FirstOrDefault() == _entity.ClrType
                && node.Arguments is [_, Expression ordering])
            {
                return Visit(ordering);
            }

            return node;
        }

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
            if (node.Parameters is [ParameterExpression p] && p.Type == _entity.ClrType)
            {
                return Visit(node.Body);
            }

            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (GetOriginOverlap(node) is not MemberExpression overlap)
            {
                return node;
            }

            string name = ExpressionHelpers.GetLineageName(overlap);

            return Expression.Constant(name);
        }

        private MemberExpression? GetOriginOverlap(MemberExpression node)
        {
            using var e = ExpressionHelpers
                .GetLineage(node)
                .Reverse()
                .GetEnumerator();

            if (!e.MoveNext())
            {
                return null;
            }

            var longestChain = e.Current;
            var nav = _entity.FindNavigation(longestChain.Member);

            if (nav is null or { IsCollection: true })
            {
                return null;
            }

            while (e.MoveNext())
            {
                nav = nav.TargetEntityType.FindNavigation(e.Current.Member);

                if (nav is null or { IsCollection: true })
                {
                    break;
                }

                longestChain = e.Current;
            }

            return longestChain;
        }
    }

    #endregion
}
