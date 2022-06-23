using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query;

internal class OriginQueryTranslationVisitor : ExpressionVisitor
{
    private readonly ParameterExpression _orderBy;

    public OriginQueryTranslationVisitor(ParameterExpression orderBy)
    {
        _orderBy = orderBy;
    }

    [return: NotNullIfNotNull("node")]
    public override Expression? Visit(Expression? node)
    {
        var visited = base.Visit(node);

        if (visited is OriginQueryExpression query && query.Type != _orderBy.Type)
        {
            return node;
        }

        return visited;
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is QueryRootExpression root)
        {
            return new OriginQueryExpression(root, Expression.Constant(null), key: null, selector: null);
        }

        return base.VisitExtension(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (base.Visit(node.Arguments.ElementAtOrDefault(0)) is not OriginQueryExpression query)
        {
            return node;
        }

        if (ExpressionHelpers.IsAfter(node)
            && node.Arguments.ElementAtOrDefault(1) is ConstantExpression origin
            && base.Visit(node.Arguments.ElementAtOrDefault(2)) is var key)
        {
            return query.Update(query.Root, origin, key as LambdaExpression, query.Selector);
        }

        if (IsOrderTranslation(query, node)
            && base.Visit(node.Arguments[1]) is LambdaExpression selector)
        {
            return query.Update(query.Root, query.Origin, query.Key, selector);
        }

        return query;
    }

    private bool IsOrderTranslation(OriginQueryExpression query, MethodCallExpression call)
        => query.Type != _orderBy.Type
            && call.Method == QueryableMethods.Select.MakeGenericMethod(query.Type, _orderBy.Type);

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType is ExpressionType.Quote)
        {
            return node.Operand;
        }

        return node;
    }
}

internal class FindOrderParameterVisitor : ExpressionVisitor
{
    private static FindOrderParameterVisitor? _instance;
    public static FindOrderParameterVisitor Instance => _instance ??= new();

    private bool _foundSeek;

    [return: NotNullIfNotNull("node")]
    public override Expression? Visit(Expression? node)
    {
        _foundSeek = SeekByNotCalled(node);

        return base.Visit(node);
    }

    private static bool SeekByNotCalled(Expression? node)
        => FindSeekByVisitor.Instance.Visit(node) is not MethodCallExpression call
            || ExpressionHelpers.IsSeekBy(call) is false;

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsOrderedMethod(node))
        {
            if (!_foundSeek)
            {
                return node;
            }

            return base.Visit(node.Arguments[1]);
        }

        if (ExpressionHelpers.IsSeekBy(node))
        {
            _foundSeek = true;
        }

        if (node.Arguments.ElementAtOrDefault(0) is Expression parent)
        {
            return base.Visit(parent);
        }

        return node;
    }

    protected override Expression VisitLambda<TFunc>(Expression<TFunc> node)
    {
        if (node.Parameters.Count == 1)
        {
            return node.Parameters[0];
        }

        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType is ExpressionType.Quote)
        {
            return base.Visit(node.Operand);
        }

        return node;
    }

    private sealed class FindSeekByVisitor : ExpressionVisitor
    {
        private static FindSeekByVisitor? _instance;
        public static FindSeekByVisitor Instance => _instance ??= new();

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (ExpressionHelpers.IsSeekBy(node))
            {
                return node;
            }

            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
            {
                return node;
            }

            return Visit(parent);
        }
    }
}

internal class OriginIncludeVisitor : ExpressionVisitor
{
    public static IReadOnlyCollection<string> Scan(Expression node, IReadOnlyEntityType? entityType = null)
    {
        var findIncludes = new OriginIncludeVisitor(entityType);

        _ = findIncludes.Visit(node);

        return findIncludes._includes;
    }

    private readonly FindIncludeVisitor? _findInclude;
    private readonly HashSet<string> _includes;

    private OriginIncludeVisitor(IReadOnlyEntityType? entity)
    {
        _findInclude = entity is null ? null : new FindIncludeVisitor(entity);
        _includes = new HashSet<string>();
    }

    [return: NotNullIfNotNull("node")]
    public new Expression? Visit(Expression? node)
    {
        _includes.Clear();

        return base.Visit(node);
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

            var overlapChain = ExpressionHelpers
                .GetLineage(overlap)
                .Reverse()
                .Select(m => m.Member.Name);

            return Expression.Constant(string.Join('.', overlapChain));
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
