using System;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query;

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

internal sealed class RemoveSeekVisitor : ExpressionVisitor
{
    private static RemoveSeekVisitor? _instance;
    public static RemoveSeekVisitor Instance => _instance ??= new();

    protected override Expression VisitExtension(Expression node)
    {
        if (node is SeekExpression seek)
        {
            return seek.Query;
        }

        return node;
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
    private readonly ConstantExpression _origin;

    public ChangeSeekOriginVisitor(ConstantExpression newOrigin)
    {
        _origin = newOrigin;
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is SeekExpression seek)
        {
            return seek.Update(seek.Query, _origin, seek.Direction, seek.Size);
        }

        return base.VisitExtension(node);
    }
}

internal sealed class ExpandSeekVisitor : ExpressionVisitor
{
    private readonly IQueryProvider _provider;

    public ExpandSeekVisitor(IQueryProvider provider)
    {
        _provider = provider;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ExpressionHelpers.IsToSeekList(node)
            && node.Arguments.FirstOrDefault() is Expression parent)
        {
            return Visit(parent);
        }

        return base.VisitMethodCall(node);
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

            if (ExpressionHelpers.IsOrderedMethod(node))
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
