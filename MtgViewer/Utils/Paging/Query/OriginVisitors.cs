using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query;

internal class OriginTranslationVisitor : ExpressionVisitor
{
    private readonly IQueryProvider _source;
    private readonly ParameterExpression _orderBy;
    private bool _isCreatedQuery;

    public OriginTranslationVisitor(IQueryProvider source, ParameterExpression orderBy)
    {
        _source = source;
        _orderBy = orderBy;
    }

    [return: NotNullIfNotNull("node")]
    public override Expression? Visit(Expression? node)
    {
        _isCreatedQuery = false;

        var visited = base.Visit(node);

        if (visited is ConstantExpression || _isCreatedQuery)
        {
            return visited;
        }

        return Expression.Constant(null, _orderBy.Type);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsSeekBy(node)
            && node.Method.GetGenericArguments()[0] == _orderBy.Type)
        {
            return node.Arguments[0];
        }

        if (base.Visit(node.Arguments.ElementAtOrDefault(0)) is not Expression parent)
        {
            return node;
        }

        if (!ExpressionHelpers.IsAfter(node))
        {
            return parent;
        }

        var afterArgument = AfterVisitor.Scan(_orderBy, node.Arguments[1]);

        if (afterArgument is ConstantExpression origin)
        {
            return origin;
        }

        if (afterArgument is not LambdaExpression predicate)
        {
            return node;
        }

        _isCreatedQuery = true;

        return _source
            .CreateQuery(parent)
            .Where(predicate)
            .Expression;
    }

    private sealed class AfterVisitor : ExpressionVisitor
    {
        public static Expression Scan(ParameterExpression orderBy, Expression node)
        {
            var visitor = new AfterVisitor(orderBy);

            return visitor.Visit(node);
        }

        private readonly ParameterExpression _orderBy;

        private AfterVisitor(ParameterExpression orderBy)
        {
            _orderBy = orderBy;
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

            return node.Update(body, node.Parameters);
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
            if (MemberEvaluationVisitor.Instance.Visit(node) is not LambdaExpression eval)
            {
                return base.VisitMember(node);
            }

            // keep eye on, could be slower than just pass thru

            if (eval.Compile().DynamicInvoke() is null)
            {
                return Expression.Constant(null, _orderBy.Type);
            }

            return base.VisitMember(node);
        }
    }

    private sealed class MemberEvaluationVisitor : ExpressionVisitor
    {
        private static MemberEvaluationVisitor? _instance;
        public static MemberEvaluationVisitor Instance => _instance ??= new();

        [return: NotNullIfNotNull("node")]
        public override Expression? Visit(Expression? node)
        {
            if (base.Visit(node) is MemberExpression member)
            {
                return Expression.Lambda(member);
            }

            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is ParameterExpression parameter)
            {
                return parameter;
            }

            return node;
        }
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

internal sealed class FindMemberParameter : ExpressionVisitor
{
    private static FindMemberParameter? _instance;
    public static FindMemberParameter Instance => _instance ??= new();

    protected override Expression VisitMember(MemberExpression node)
    {
        if (Visit(node.Expression) is Expression parent)
        {
            return parent;
        }

        return node;
    }
}

internal sealed class FindSelectVisitor : ExpressionVisitor
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
            && node.Method.GetGenericArguments().ElementAtOrDefault(1) == _resultType
            && node.Arguments.ElementAtOrDefault(1) is Expression select)
        {
            return Visit(select);
        }

        if (method == QueryableMethods.SelectManyWithoutCollectionSelector
            && node.Method.GetGenericArguments().ElementAtOrDefault(1) == _resultType
            && node.Arguments.ElementAtOrDefault(1) is Expression selectMany)
        {
            return Visit(selectMany);
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

internal sealed class FindQueryRootVisitor : ExpressionVisitor
{
    private static FindQueryRootVisitor? _instance;
    public static FindQueryRootVisitor Instance => _instance ??= new();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        return Visit(parent);
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is QueryRootExpression root)
        {
            return root;
        }

        return node;
    }
}

internal sealed class OriginIncludeVisitor : ExpressionVisitor
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
