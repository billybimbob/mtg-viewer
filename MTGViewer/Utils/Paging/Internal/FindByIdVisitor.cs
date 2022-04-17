using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

internal class FindByIdVisitor : ExpressionVisitor
{
    private readonly FindIncludeVisitor? _findInclude;
    private readonly HashSet<string> _include;

    public FindByIdVisitor(IReadOnlyEntityType? entity = null)
    {
        _findInclude = entity is null ? null : new FindIncludeVisitor(entity);
        _include = new HashSet<string>();
    }

    public IReadOnlyCollection<string> Include => _include;

    [return: NotNullIfNotNull("node")]
    public new Expression? Visit(Expression? node)
    {
        _include.Clear();

        return base.Visit(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent
            || node is { Method.IsGenericMethod: false })
        {
            return node;
        }

        var visitedParent = base.Visit(parent);
        var method = node.Method.GetGenericMethodDefinition();

        if (method == QueryableMethods.Where)
        {
            return node.Update(
                node.Object,
                node.Arguments.Skip(1).Prepend(visitedParent));
        }

        if (_findInclude?.Visit(node) is ConstantExpression { Value: string includeChain })
        {
            const StringComparison ordinal = StringComparison.Ordinal;

            _include.RemoveWhere(s => includeChain.StartsWith(s, ordinal));
            _include.Add(includeChain);
        }

        return visitedParent;
    }
}

internal class FindIncludeVisitor : ExpressionVisitor
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
