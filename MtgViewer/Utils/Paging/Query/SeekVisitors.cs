using System;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class SeekTranslationVisitor : ExpressionVisitor
{
    private static SeekTranslationVisitor? _instance;
    public static SeekTranslationVisitor Instance => _instance ??= new();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsToSeekList(node)
            && node.Arguments.FirstOrDefault() is Expression parent)
        {
            return Visit(parent);
        }

        return TranslateSeekBy(node)
            ?? TranslateAfter(node)
            ?? TranslateTake(node)
            ?? base.VisitMethodCall(node);
    }

    private static SeekExpression? TranslateSeekBy(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsSeekBy(node)
            && node.Arguments.ElementAtOrDefault(1) is ConstantExpression { Value: SeekDirection direction }
            && node.Arguments.ElementAtOrDefault(0) is Expression query
            && query.Type.GenericTypeArguments.ElementAtOrDefault(0) is Type entityType)
        {
            return new SeekExpression(
                query, Expression.Constant(null, entityType), direction, size: null);
        }

        return null;
    }

    private SeekExpression? TranslateAfter(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsAfter(node)
            && node.Arguments.ElementAtOrDefault(1) is ConstantExpression origin
            && Visit(node.Arguments.ElementAtOrDefault(0)) is SeekExpression seek)
        {
            return seek.Update(seek.Query, origin, seek.Direction, seek.Size);
        }

        return null;
    }

    private SeekExpression? TranslateTake(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsThenTake(node)
            && node.Arguments.ElementAtOrDefault(1) is ConstantExpression { Value: int count }
            && Visit(node.Arguments.ElementAtOrDefault(0)) is SeekExpression seek)
        {
            return seek.Update(seek.Query, seek.Origin, seek.Direction, count);
        }

        return null;
    }
}

internal sealed class LookAheadSeekVisitor : ExpressionVisitor
{
    private static LookAheadSeekVisitor? _instance;
    public static LookAheadSeekVisitor Instance => _instance ??= new();

    protected override Expression VisitExtension(Expression node)
    {
        if (node is SeekExpression seek)
        {
            return seek.Update(seek.Query, seek.Origin, seek.Direction, seek.Size + 1);
        }

        return base.VisitExtension(node);
    }
}

internal sealed class ChangeSeekOriginVisitor : ExpressionVisitor
{
    private readonly object? _origin;

    public ChangeSeekOriginVisitor(object? newOrigin)
    {
        _origin = newOrigin;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsAfter(node)
            && node.Arguments.ElementAtOrDefault(0) is Expression source
            && node.Method.GetGenericArguments().ElementAtOrDefault(0) is Type entityType)
        {
            return Expression.Call(
                instance: null,
                method: PagingExtensions.AfterReference
                    .MakeGenericMethod(entityType),
                arg0: source,
                arg1: Expression.Constant(_origin, entityType));
        }

        return base.VisitMethodCall(node);
    }
}

internal sealed class ExpandSeekVisitor : ExpressionVisitor
{
    private readonly IQueryProvider _provider;

    public ExpandSeekVisitor(IQueryProvider provider)
    {
        _provider = provider;
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is SeekExpression seek)
        {
            return InsertExpandedSeekVisitor.Visit(_provider, seek);
        }

        return node;
    }

    private sealed class InsertExpandedSeekVisitor : ExpressionVisitor
    {
        public static Expression Visit(IQueryProvider provider, SeekExpression node)
        {
            var filter = OriginFilter.Build(node);

            var insertExpanded = new InsertExpandedSeekVisitor(
                provider,
                filter,
                node.Direction,
                node.Size);

            return insertExpanded.Visit(node);
        }

        private readonly IQueryProvider _provider;
        private readonly LambdaExpression? _filter;
        private readonly SeekDirection _direction;
        private readonly int? _size;

        private InsertExpandedSeekVisitor(
            IQueryProvider provider,
            LambdaExpression? filter,
            SeekDirection direction,
            int? size)
        {
            _provider = provider;
            _filter = filter;
            _direction = direction;
            _size = size;
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is not SeekExpression seek)
            {
                return base.VisitExtension(node);
            }

            var query = Visit(seek.Query);

            if (ExpressionEqualityComparer.Instance.Equals(query, seek.Query))
            {
                return seek;
            }

            return query;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // expanded query will immediately follow the OrderBy chain

            if (node.Type.IsAssignableTo(typeof(IOrderedQueryable)))
            {
                return ExpandToQuery(node).Expression;
            }

            return base.VisitMethodCall(node);
        }

        private IQueryable ExpandToQuery(MethodCallExpression node)
        {
            var query = _provider.CreateQuery(node);

            return (_direction, _filter, _size) switch
            {
                (SeekDirection.Forward, not null, int size) => query
                    .Where(_filter)
                    .Take(size),

                (SeekDirection.Backwards, not null, int size) => query
                    .Reverse()
                    .Where(_filter)
                    .Take(size)
                    .Reverse(),

                (SeekDirection.Forward, not null, null) => query
                    .Where(_filter),

                (SeekDirection.Backwards, not null, null) => query
                    .Reverse()
                    .Where(_filter)
                    .Reverse(),

                (SeekDirection.Forward, null, int size) => query
                    .Take(size),

                (SeekDirection.Backwards, null, int size) => query
                    .Reverse()
                    .Take(size)
                    .Reverse(),

                _ => query
            };
        }
    }
}

internal sealed class FindSeekVisitor : ExpressionVisitor
{
    private static FindSeekVisitor? _instance;
    public static FindSeekVisitor Instance => _instance ??= new();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is Expression parent)
        {
            return Visit(parent);
        }

        return node;
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is SeekExpression seek)
        {
            return seek;
        }

        return node;
    }
}
