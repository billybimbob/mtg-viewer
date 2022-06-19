using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query;

internal class FindOriginQueryVisitor : ExpressionVisitor
{
    private readonly ParameterExpression _orderBy;

    public FindOriginQueryVisitor(ParameterExpression orderBy)
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
            return new OriginQueryExpression(root, Expression.Constant(null), null);
        }

        if (node is not SeekExpression seek)
        {
            return node;
        }

        if (base.Visit(seek.Query) is OriginQueryExpression query)
        {
            return query.Update(query.Root, seek.Origin, query.Selector);
        };

        return seek;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (base.Visit(node.Arguments.ElementAtOrDefault(0))
            is not OriginQueryExpression query)
        {
            return node;
        }

        if (IsOrderTranslation(query, node)
            && base.Visit(node.Arguments[1]) is LambdaExpression selector)
        {
            return query.Update(query.Root, query.Origin, selector);
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
        _foundSeek = false;

        return base.Visit(node);
    }

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

    protected override Expression VisitExtension(Expression node)
    {
        if (node is not SeekExpression seek)
        {
            return node;
        }

        _foundSeek = true;

        return base.Visit(seek.Query);
    }
}

internal class OriginIncludeVisitor : ExpressionVisitor
{
    private readonly FindIncludeVisitor? _findInclude;
    private readonly HashSet<string> _include;

    public OriginIncludeVisitor(IReadOnlyEntityType? entity = null)
    {
        _findInclude = entity is null ? null : new FindIncludeVisitor(entity);
        _include = new HashSet<string>();
    }

    public IReadOnlyCollection<string> Includes => _include;

    [return: NotNullIfNotNull("node")]
    public new Expression? Visit(Expression? node)
    {
        _include.Clear();

        return base.Visit(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        var visitedParent = base.Visit(parent);

        if (node is { Method.IsGenericMethod: false })
        {
            return visitedParent;
        }

        var method = node.Method.GetGenericMethodDefinition();

        if (_findInclude?.Visit(node) is ConstantExpression { Value: string includeChain })
        {
            const StringComparison ordinal = StringComparison.Ordinal;

            _include.RemoveWhere(s => includeChain.StartsWith(s, ordinal));
            _include.Add(includeChain);
        }

        return visitedParent;
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is SeekExpression seek)
        {
            return base.Visit(seek.Query);
        }

        return base.VisitExtension(node);
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
