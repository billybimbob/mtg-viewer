using System;
using System.Linq;
using System.Linq.Expressions;

using EntityFrameworkCore.Paging.Extensions;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class FindSeekVisitor : ExpressionVisitor
{
    private static FindSeekVisitor? _instance;
    public static FindSeekVisitor Instance => _instance ??= new();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        if (parent is SeekExpression seek)
        {
            return seek;
        }

        return Visit(parent);
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

internal sealed class ExpandSeekVisitor : ExpressionVisitor
{
    private readonly IQueryProvider _provider;
    private readonly ConstantExpression _origin;

    public ExpandSeekVisitor(IQueryProvider provider, ConstantExpression origin)
    {
        _provider = provider;
        _origin = origin;
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is not SeekExpression seek)
        {
            return node;
        }

        var insertExpansion = new InsertExpandedSeekVisitor(_provider, _origin, seek);

        return insertExpansion.Visit(seek.Query);
    }

    private sealed class InsertExpandedSeekVisitor : ExpressionVisitor
    {
        private readonly IQueryProvider _provider;
        private readonly ConstantExpression _origin;
        private readonly SeekExpression _seek;

        public InsertExpandedSeekVisitor(
            IQueryProvider provider,
            ConstantExpression origin,
            SeekExpression seek)
        {
            _provider = provider;
            _origin = origin;
            _seek = seek;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Type.GenericTypeArguments.FirstOrDefault() == _origin.Type
                || _origin.Value is null)
            {
                return ExpandedQuery(node).Expression;
            }

            if (node.Arguments.FirstOrDefault() is not Expression parent)
            {
                return node;
            }

            return node.Update(
                node.Object,
                node.Arguments
                    .Skip(1)
                    .Prepend(Visit(parent)));
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is SeekExpression seek)
            {
                return Visit(seek.Query);
            }

            if (node is QueryRootExpression root
                && (root.EntityType.ClrType == _origin.Type || _origin.Value is null))
            {
                return ExpandedQuery(root).Expression;
            }

            return node;
        }

        private IQueryable ExpandedQuery(Expression node)
        {
            var query = _provider.CreateQuery(node);

            var filter = OriginFilter.Build(query, _origin, _seek.Direction);

            return (_seek.Direction, filter, _seek.Size) switch
            {
                (SeekDirection.Forward, not null, int size) => query
                    .Where(filter)
                    .Take(size),

                (SeekDirection.Backwards, not null, int size) => query
                    .Reverse()
                    .Where(filter)
                    .Take(size)
                    .Reverse(),

                (SeekDirection.Forward, not null, null) => query
                    .Where(filter),

                (SeekDirection.Backwards, not null, null) => query
                    .Reverse()
                    .Where(filter)
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
