using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query.Infrastructure;

internal sealed class OriginIncludesVisitor : ExpressionVisitor
{
    [return: NotNullIfNotNull("node")]
    public new Expression? Visit(Expression? node)
    {
        if (base.Visit(node) is QueryRootExpression queryRoot)
        {
            var scanner = new ScanIncludesVisitor(queryRoot);
            return scanner.Visit(node);
        }

        return base.Visit(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (base.Visit(node.Arguments.ElementAtOrDefault(0)) is not Expression parent)
        {
            return node;
        }

        return parent;
    }

    private sealed class ScanIncludesVisitor : ExpressionVisitor
    {
        private readonly FindIncludeVisitor _findInclude;

        public ScanIncludesVisitor(QueryRootExpression queryRoot)
        {
            _findInclude = new FindIncludeVisitor(queryRoot.EntityType);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
            {
                return node;
            }

            if (_findInclude.Visit(node) is not ConstantExpression { Value: string includeChain })
            {
                return Visit(parent);
            }

            var includes = new HashSet<string>();

            if (Visit(parent) is ConstantExpression { Value: IEnumerable<string> parentIncludes })
            {
                includes.UnionWith(parentIncludes);
            }

            const StringComparison ordinal = StringComparison.Ordinal;

            includes.RemoveWhere(s => includeChain.StartsWith(s, ordinal));
            includes.Add(includeChain);

            return Expression.Constant(includes);
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
                && node.Arguments.Count == 2
                && node.Arguments[1] is Expression ordering)
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
            if (node.Parameters.Count == 1
                && node.Parameters[0].Type == _entity.ClrType)
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
}
