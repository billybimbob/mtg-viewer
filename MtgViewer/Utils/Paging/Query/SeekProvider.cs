using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class SeekProvider : IAsyncQueryProvider
{
    private readonly IAsyncQueryProvider _source;

    public SeekProvider(IAsyncQueryProvider source)
    {
        _source = source;
    }

    #region Create Query

    // keep eye on, weakly typed methods have not been tested
    // maybe just throw InvalidOperation instead

    IQueryable IQueryProvider.CreateQuery(Expression expression)
        => SeekQuery.Create(this, expression);

    IQueryable<TElement> IQueryProvider.CreateQuery<TElement>(Expression expression)
        => SeekQuery.Create<TElement>(this, expression);

    #endregion

    #region Execute

    object? IQueryProvider.Execute(Expression expression)
    {
        object? origin = ExecuteOrigin(expression);

        var sourceExpression = ExpandSeek(expression, origin);

        return _source.Execute(sourceExpression);
    }

    TResult IQueryProvider.Execute<TResult>(Expression expression)
    {
        if (ExpressionHelpers.IsToSeekList(expression))
        {
            return Invoke<TResult>(
                ExecuteSeekListMethod
                    .MakeGenericMethod(typeof(TResult).GenericTypeArguments[0]),
                expression);
        }

        object? origin = ExecuteOrigin(expression);

        var sourceExpression = ExpandSeek(expression, origin);

        return _source.Execute<TResult>(sourceExpression);
    }

    TResult IAsyncQueryProvider.ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
    {
        if (ExpressionHelpers.IsToSeekList(expression))
        {
            return Invoke<TResult>(
                ExecuteSeekListAsyncMethod
                    .MakeGenericMethod(typeof(TResult)
                        .GenericTypeArguments[0]
                        .GenericTypeArguments[0]),
                expression,
                cancellationToken);
        }

        if (typeof(TResult).IsGenericType
            && typeof(TResult).GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
        {
            return Invoke<TResult>(
                ExecuteAsyncEnumerableMethod
                    .MakeGenericMethod(typeof(TResult).GenericTypeArguments[0]),
                expression,
                cancellationToken);
        }

        if (typeof(TResult).IsGenericType
            && typeof(TResult).GetGenericTypeDefinition() == typeof(Task<>))
        {
            return Invoke<TResult>(
                ExecuteTaskAsyncMethod
                    .MakeGenericMethod(typeof(TResult).GenericTypeArguments[0]),
                expression,
                cancellationToken);
        }

        throw new NotSupportedException($"Unexpected result type: {typeof(TResult).Name}");
    }

    #endregion

    private TResult Invoke<TResult>(MethodInfo method, params object[] parameters)
        => (TResult)method.Invoke(this, parameters)!;

    private Expression ExpandSeek(Expression expression)
    {
        var seekExpander = new ExpandSeekVisitor(_source);

        var expanded = seekExpander.Visit(expression);

        if (FindSeekVisitor.Instance.Visit(expanded) is SeekExpression)
        {
            throw new InvalidOperationException(
                "The expression could not translate the \"SeekBy\" query, "
                + "make sure to call the \"SeekBy\" after \"OrderBy\"");
        }

        return expanded;
    }

    private Expression ExpandSeek(Expression expression, object? origin)
    {
        var changeOrigin = new ChangeSeekOriginVisitor(Expression.Constant(origin));

        var updatedOrigin = changeOrigin.Visit(expression);

        return ExpandSeek(updatedOrigin);
    }

    private static MethodInfo GetPrivateMethod(string name, params Type[] parameterTypes)
    {
        const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        return typeof(SeekProvider)
            .GetMethod(name, privateInstance, parameterTypes)!;
    }

    private static readonly MethodInfo ExecuteAsyncEnumerableMethod
        = GetPrivateMethod(nameof(ExecuteAsyncEnumerable), typeof(Expression), typeof(CancellationToken));

    private async IAsyncEnumerable<T> ExecuteAsyncEnumerable<T>(
        Expression expression, [EnumeratorCancellation] CancellationToken cancel)
    {
        object? origin = await ExecuteOriginAsync(expression, cancel)
            .ConfigureAwait(false);

        var query = _source
            .CreateQuery<T>(ExpandSeek(expression, origin))
            .AsAsyncEnumerable()
            .WithCancellation(cancel)
            .ConfigureAwait(false);

        await foreach (var entity in query)
        {
            yield return entity;
        }
    }

    private static readonly MethodInfo ExecuteTaskAsyncMethod
        = GetPrivateMethod(nameof(ExecuteTaskAsync), typeof(Expression), typeof(CancellationToken));

    private async Task<T> ExecuteTaskAsync<T>(Expression expression, CancellationToken cancel)
    {
        object? origin = await ExecuteOriginAsync(expression, cancel)
            .ConfigureAwait(false);

        var sourceExpression = ExpandSeek(expression, origin);

        return await _source.ExecuteAsync<Task<T>>(sourceExpression, cancel)
            .ConfigureAwait(false);
    }

    #region Fetch Seek List

    private static readonly MethodInfo ExecuteSeekListMethod
        = GetPrivateMethod(nameof(ExecuteSeekList), typeof(Expression));

    private SeekList<TEntity> ExecuteSeekList<TEntity>(Expression expression)
        where TEntity : class
    {
        object? origin = ExecuteOrigin(expression);

        var updatedSeek = AddSeekListValues(expression, origin);

        var items = _source
            .CreateQuery<TEntity>(ExpandSeek(updatedSeek))
            .ToList();

        var seek = FindSeekVisitor.Instance.Visit(updatedSeek) as SeekExpression;

        return CreateSeekList(items, seek);
    }

    private static readonly MethodInfo ExecuteSeekListAsyncMethod
        = GetPrivateMethod(nameof(ExecuteSeekListAsync), typeof(Expression), typeof(CancellationToken));

    private async Task<SeekList<TEntity>> ExecuteSeekListAsync<TEntity>(Expression expression, CancellationToken cancel)
        where TEntity : class
    {
        object? origin = await ExecuteOriginAsync(expression, cancel)
            .ConfigureAwait(false);

        var updatedSeek = AddSeekListValues(expression, origin);

        var items = await _source
            .CreateQuery<TEntity>(ExpandSeek(updatedSeek))
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        var seek = FindSeekVisitor.Instance.Visit(updatedSeek) as SeekExpression;

        return CreateSeekList(items, seek);
    }

    private static Expression AddSeekListValues(Expression expression, object? origin)
    {
        var changeOrigin = new ChangeSeekOriginVisitor(Expression.Constant(origin));

        var updatedOrigin = changeOrigin.Visit(expression);

        return LookAheadSeekVisitor.Instance.Visit(updatedOrigin);
    }

    private static SeekList<TEntity> CreateSeekList<TEntity>(List<TEntity> items, SeekExpression? seek)
        where TEntity : class
    {
        if (seek is null)
        {
            return new SeekList<TEntity>(
                items,
                hasPrevious: false,
                hasNext: false,
                isMissing: false);
        }

        var direction = seek.Direction;
        bool hasOrigin = seek.Origin.Value is not null;

        bool lookAhead = items.Count == seek.Size;
        int? targetSize = seek.Size - 1;

        // potential issue with extra items tracked that are not actually returned
        // keep eye on

        if (lookAhead && seek.Direction is SeekDirection.Forward)
        {
            items.RemoveAt(items.Count - 1);
        }
        else if (lookAhead && seek.Direction is SeekDirection.Backwards)
        {
            items.RemoveAt(0);
        }

        return new SeekList<TEntity>(items, direction, hasOrigin, lookAhead, targetSize);
    }

    #endregion

    #region Fetch Origin

    private object? ExecuteOrigin(Expression source)
    {
        if (FindOriginQuery(source) is not OriginQueryExpression query)
        {
            return null;
        }

        if (TryGetLocalOrigin(query, out object? local))
        {
            return local;
        }

        if (query.Origin.Value is not object key)
        {
            return null;
        }

        return CreateOriginQuery(query, key, source)
            .SingleOrDefault();
    }

    private async Task<object?> ExecuteOriginAsync(Expression source, CancellationToken cancel)
    {
        if (FindOriginQuery(source) is not OriginQueryExpression query)
        {
            return null;
        }

        if (TryGetLocalOrigin(query, out object? local))
        {
            return local;
        }

        if (query.Origin.Value is not object key)
        {
            return null;
        }

        return await CreateOriginQuery(query, key, source)
            .SingleOrDefaultAsync(cancel)
            .ConfigureAwait(false);
    }

    private static OriginQueryExpression? FindOriginQuery(Expression source)
    {
        if (FindOrderParameterVisitor.Instance.Visit(source)
            is not ParameterExpression orderBy)
        {
            return null;
        }

        var findOriginQuery = new FindOriginQueryVisitor(orderBy);

        return findOriginQuery.Visit(source) as OriginQueryExpression;
    }

    private static bool TryGetLocalOrigin(OriginQueryExpression query, out object? origin)
    {
        if (query.Origin.Type == query.Type)
        {
            origin = query.Origin.Value;
            return true;
        }

        if (query.Origin.Type != query.Selector?.Parameters.FirstOrDefault()?.Type)
        {
            origin = null;
            return false;
        }

        origin = query.Selector
            .Compile()
            .DynamicInvoke(query.Origin.Value);

        return true;
    }

    private IQueryable CreateOriginQuery(
        OriginQueryExpression query,
        object key,
        Expression source)
        => query.Selector is not null
            ? CreateOriginQuery(query.Root, key, query.Selector)
            : CreateOriginQuery(query.Root, key, source);

    private IQueryable CreateOriginQuery(
        QueryRootExpression root,
        object key,
        LambdaExpression selector)
    {
        var sourceParameter = Expression
            .Parameter(
                type: root.EntityType.ClrType,
                name: root.EntityType.ClrType.Name[0].ToString().ToLowerInvariant());

        var keyProperty = Expression
            .Property(sourceParameter, GetKeyProperty(root, key));

        var equalsToKey = Expression
            .Equal(
                keyProperty,
                Expression.Constant(key, keyProperty.Type));

        return _source
            .CreateQuery(root)
            .Where(Expression.Lambda(equalsToKey, sourceParameter))
            .OrderBy(Expression.Lambda(keyProperty, sourceParameter))
            .Select(selector);
    }

    private static PropertyInfo GetKeyProperty(QueryRootExpression root, object key)
    {
        var getKey = root.EntityType.GetKeyInfo();

        var keyType = Nullable.GetUnderlyingType(key.GetType()) ?? key.GetType();

        if (getKey.PropertyType != keyType)
        {
            throw new InvalidOperationException(
                $"Key type ({key.GetType().Name}) is not expected type {getKey.PropertyType.Name}");
        }

        return getKey;
    }

    private IQueryable CreateOriginQuery(
        QueryRootExpression root,
        object key,
        Expression source)
    {
        var originInclude = new OriginIncludeVisitor(root.EntityType);

        var query = _source
            .CreateQuery(originInclude.Visit(source));

        foreach (string include in originInclude.Includes)
        {
            query = query.Include(include);
        }

        var originParameter = Expression
            .Parameter(
                type: query.ElementType,
                name: query.ElementType.Name[0].ToString().ToLowerInvariant());

        var keyProperty = Expression
            .Property(originParameter, GetKeyProperty(root, key));

        var equalsToKey = Expression
            .Equal(
                keyProperty,
                Expression.Constant(key, keyProperty.Type));

        return query
            .Where(Expression.Lambda(equalsToKey, originParameter))
            .OrderBy(Expression.Lambda(keyProperty, originParameter))
            .AsNoTracking();
    }

    #endregion
}
