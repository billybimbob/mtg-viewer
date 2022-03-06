using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;


public class SeekBuilder<TEntity, TOrigin>
    where TEntity : class
    where TOrigin : class
{
    private readonly IQueryable<TEntity> _query;
    private readonly QueryRootExpression _root;

    private readonly int _pageSize;
    private readonly bool _backtrack;

    private readonly object? _key;


    public SeekBuilder(IQueryable<TEntity> query, int pageSize, bool backtrack)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (FindRootQuery.Instance.Visit(query.Expression)
            is not QueryRootExpression root)
        {
            throw new ArgumentException(nameof(query));
        }

        _query = query;
        _root = root;

        _pageSize = pageSize;
        _backtrack = backtrack;
    }


    private SeekBuilder(IQueryable<TEntity> query, int pageSize, bool backtrack, object? key)
        : this(query, pageSize, backtrack)
    {
        _key = key;
    }


    private FindByIdVisitor? _findById;
    private FindByIdVisitor FindById => _findById ??= new(_root);


    public SeekBuilder<TEntity, TNewOrigin> WithOrigin<TNewOrigin>(object? key)
        where TNewOrigin : class
    {
        return new SeekBuilder<TEntity, TNewOrigin>(_query, _pageSize, _backtrack, key);
    }


    public async Task<SeekList<TEntity>> ToSeekListAsync(CancellationToken cancel = default)
    {
        var origin = await GetOriginAsync(cancel).ConfigureAwait(false);

        return await _query
            .SeekOrigin(origin, _pageSize, _backtrack)
            .ToSeekListAsync(cancel)
            .ConfigureAwait(false);
    }


    private async ValueTask<TOrigin?> GetOriginAsync(CancellationToken cancel)
    {
        if (_key is TOrigin keyOrigin)
        {
            return keyOrigin;
        }

        if (_key is not object key)
        {
            return null;
        }

        var entityId = ExpressionHelpers.GetKeyProperty<TOrigin>();

        if (key.GetType() != entityId.PropertyType)
        {
            throw new ArgumentException($"{nameof(key)} is the not correct key type");
        }

        var entityParameter = Expression.Parameter(
            typeof(TOrigin), typeof(TOrigin).Name[0].ToString().ToLower());

        var propertyId = Expression.Property(entityParameter, entityId);

        var equalSeek = Expression.Lambda<Func<TOrigin, bool>>(
            Expression.Equal(propertyId, Expression.Constant(key)),
            entityParameter);

        return await GetOriginQuery(entityParameter, propertyId)
            .AsNoTracking()
            .SingleOrDefaultAsync(equalSeek, cancel)
            .ConfigureAwait(false);
    }


    private IQueryable<TOrigin> GetOriginQuery(ParameterExpression parameter, MemberExpression key)
    {
        if (parameter.Type != typeof(TOrigin))
        {
            throw new ArgumentException(nameof(parameter));
        }

        var findId = FindById.Visit(_query.Expression);
        var lambdaId = Expression.Lambda(key, parameter);

        var orderedId = Expression.Call(
            QueryableMethods.OrderBy.MakeGenericMethod(typeof(TOrigin), key.Type),
            FindById.Visit(_query.Expression),
            Expression.Quote(lambdaId));

        var originQuery = _query.Provider.CreateQuery<TOrigin>(orderedId);

        foreach (var include in FindById.Include)
        {
            originQuery = originQuery.Include(include);
        }

        return originQuery;
    }



    private class FindByIdVisitor : ExpressionVisitor
    {
        private readonly FindIncludeVisitor _findInclude;
        private readonly HashSet<string> _include;

        public FindByIdVisitor(QueryRootExpression root)
        {
            _findInclude = new(root);
            _include = new();
        }


        public IReadOnlyCollection<string> Include => _include;


        [return: NotNullIfNotNull("node")]
        public override Expression? Visit(Expression? node)
        {
            _include.Clear();

            return base.Visit(node);
        }


        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Arguments.ElementAtOrDefault(0) is not Expression parent
                || !node.Method.IsGenericMethod
                || node.Method.GetGenericMethodDefinition() is not MethodInfo method)
            {
                return node;
            }

            var visitedParent = base.Visit(parent);

            if (method == QueryableMethods.Where)
            {
                return node.Update(
                    node.Object,
                    node.Arguments.Skip(1).Prepend(visitedParent));
            }

            // todo: add include statements

            if (_findInclude.Visit(node) is ConstantExpression constant
                && constant.Value is string includeChain)
            {
                const StringComparison ordinal = StringComparison.Ordinal;

                _include.RemoveWhere(s => includeChain.StartsWith(s, ordinal));
                _include.Add(includeChain);
            }

            return visitedParent;
        }
    }



    private class FindIncludeVisitor : ExpressionVisitor
    {
        private readonly QueryRootExpression _root;

        public FindIncludeVisitor(QueryRootExpression root)
        {
            _root = root;
        }


        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (ExpressionHelpers.IsOrderedMethod(node)
                && node.Method.GetGenericArguments().FirstOrDefault() == typeof(TOrigin)
                && node.Arguments.ElementAtOrDefault(1) is Expression ordering)
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
                && node.Parameters.ElementAtOrDefault(0)?.Type == typeof(TOrigin))
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
            var nav = _root.EntityType.FindNavigation(longestChain.Member);

            if (longestChain.Expression?.Type != typeof(TOrigin)
                || nav is null
                || nav.IsCollection)
            {
                return null;
            }

            while (e.MoveNext())
            {
                nav = nav.TargetEntityType.FindNavigation(e.Current.Member);

                if (nav is null || nav.IsCollection)
                {
                    break;
                }

                longestChain = e.Current;
            }

            return longestChain;
        }

    }
}