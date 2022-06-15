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

internal sealed class SeekProvider<TSource> : IAsyncQueryProvider
{
    private readonly IAsyncQueryProvider _source;

    public SeekProvider(IAsyncQueryProvider source)
    {
        _source = source;
    }

    #region Weakly Typed

    // keep eye on, these weakly typed methods have not been tested
    // maybe just throw InvalidOperation instead

    public IQueryable CreateQuery(Expression expression)
    {
        return FindSeekVisitor.Instance.Visit(expression) is SeekExpression seek
            ? CreateSeekQuery(expression, seek)
            : CreateSeekQuery(expression);
    }

    private IQueryable CreateSeekQuery(Expression expression, SeekExpression seek)
    {
        var removedSeek = RemoveSeekVisitor.Instance.Visit(expression);

        var newQuery = _source.CreateQuery(removedSeek);

        var newSeek = seek.Update(newQuery.Expression, seek.Origin, seek.Direction, seek.Size);

        return SeekQueryable.Create(this, newSeek);
    }

    private IQueryable CreateSeekQuery(Expression expression)
    {
        var newQuery = _source.CreateQuery(expression);

        return SeekQueryable.Create(this, newQuery.Expression);
    }

    public object? Execute(Expression expression)
    {
        object? origin = ExecuteOrigin(expression);

        var query = CreateExpandedQuery(expression, origin);

        return _source.Execute(query.Expression);
    }

    #endregion

    #region Create Query (strongly typed)

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        return FindSeekVisitor.Instance.Visit(expression) is SeekExpression seek
            ? CreateSeekQuery<TElement>(expression, seek)
            : CreateSeekQuery<TElement>(expression);
    }

    private IQueryable<TElement> CreateSeekQuery<TElement>(Expression expression, SeekExpression seek)
    {
        var removedSeek = RemoveSeekVisitor.Instance.Visit(expression);

        var newQuery = _source.CreateQuery<TElement>(removedSeek);

        var newSeek = seek.Update(newQuery.Expression, seek.Origin, seek.Direction, seek.Size);

        var newProvider = GetNewQueryProvider<TElement>();

        return SeekQueryable.Create(newProvider, newSeek);
    }

    private IQueryable<TElement> CreateSeekQuery<TElement>(Expression expression)
    {
        var newQuery = _source.CreateQuery<TElement>(expression);

        var newProvider = GetNewQueryProvider<TElement>();

        return SeekQueryable.Create(newProvider, newQuery.Expression);
    }

    private SeekProvider<TElement> GetNewQueryProvider<TElement>()
    {
        if (typeof(TElement) == typeof(TSource))
        {
            return (SeekProvider<TElement>)(object)this;
        }

        return new SeekProvider<TElement>(_source);
    }

    #endregion

    public TResult Execute<TResult>(Expression expression)
    {
        if (IsSeekListExecute<TResult>(expression))
        {
            return ExecuteDynamic<TResult>(
                nameof(ExecuteSeekList),
                new[] { typeof(TResult).GenericTypeArguments[0] },
                expression);
        }

        object? origin = ExecuteOrigin(expression);

        var query = CreateExpandedQuery(expression, origin);

        return _source.Execute<TResult>(query.Expression);
    }

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
    {
        if (IsSeekListExecute<TResult>(expression))
        {
            return ExecuteDynamic<TResult>(
                nameof(ExecuteSeekListAsync),
                new[] { typeof(TResult).GenericTypeArguments[0].GenericTypeArguments[0] },
                expression,
                cancellationToken);
        }

        if (typeof(TResult).IsGenericType
            && typeof(TResult).GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
        {
            return ExecuteDynamic<TResult>(
                nameof(ExecuteAsyncEnumerable),
                new[] { typeof(TResult).GenericTypeArguments[0] },
                expression, cancellationToken);
        }

        if (typeof(TResult).IsGenericType
            && typeof(TResult).GetGenericTypeDefinition() == typeof(Task<>))
        {
            return ExecuteDynamic<TResult>(
                nameof(ExecuteTaskAsync),
                new[] { typeof(TResult).GenericTypeArguments[0] },
                expression,
                cancellationToken);
        }

        throw new NotSupportedException($"Unexpected result type: {typeof(TResult).Name}");
    }

    private async IAsyncEnumerable<T> ExecuteAsyncEnumerable<T>(
        Expression expression, [EnumeratorCancellation] CancellationToken cancel)
    {
        object? origin = await ExecuteOriginAsync(expression, cancel).ConfigureAwait(false);

        var query = CreateExpandedQuery(expression, origin)
            .Cast<T>()
            .AsAsyncEnumerable()
            .WithCancellation(cancel)
            .ConfigureAwait(false);

        await foreach (var entity in query)
        {
            yield return entity;
        }
    }

    private async Task<T> ExecuteTaskAsync<T>(Expression expression, CancellationToken cancel)
    {
        object? origin = await ExecuteOriginAsync(expression, cancel)
            .ConfigureAwait(false);

        var query = CreateExpandedQuery(expression, origin);

        return await _source.ExecuteAsync<Task<T>>(query.Expression, cancel)
            .ConfigureAwait(false);
    }

    private TResult ExecuteDynamic<TResult>(
        string executeName,
        Type[] methodTypes,
        params object[] parameters)
    {
        const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        var parameterTypes = parameters.Select(o => o.GetType()).ToArray();

        return (TResult)typeof(SeekProvider<>)
            .MakeGenericType(typeof(TSource))

            .GetMethod(executeName, privateInstance, parameterTypes)!
            .MakeGenericMethod(methodTypes)

            .Invoke(this, parameters)!;
    }

    private IQueryable CreateExpandedQuery(Expression expression, object? origin)
    {
        var originUpdate = new ExpandSeekVisitor(_source, Expression.Constant(origin));

        var expandedExpression = originUpdate.Visit(expression);

        return _source.CreateQuery(expandedExpression);
    }

    #region Fetch Seek List

    private SeekList<TEntity> ExecuteSeekList<TEntity>(Expression expression)
        where TEntity : class
    {
        object? origin = ExecuteOrigin(expression);

        var seek = GetLookAheadSeek(expression, origin);

        var items = CreateItemsQuery(expression, seek)
            .Cast<TEntity>()
            .ToList();

        return CreateSeekList(items, seek);
    }

    private async Task<SeekList<TEntity>> ExecuteSeekListAsync<TEntity>(Expression expression, CancellationToken cancel)
        where TEntity : class
    {
        object? origin = await ExecuteOriginAsync(expression, cancel)
            .ConfigureAwait(false);

        var seek = GetLookAheadSeek(expression, origin);

        var items = await CreateItemsQuery(expression, seek)
            .Cast<TEntity>()
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        return CreateSeekList(items, seek);
    }

    private static bool IsSeekListExecute<TResult>(Expression expression)
    {
        if (typeof(TSource).IsValueType)
        {
            return false;
        }

        if (expression is not MethodCallExpression call)
        {
            return false;
        }

        var toSeekListMethod = PagingExtensions.ToSeekListMethodInfo.MakeGenericMethod(typeof(TSource));

        if (call.Method != toSeekListMethod)
        {
            return false;
        }

        var seekListType = typeof(SeekList<>).MakeGenericType(typeof(TSource));

        return typeof(TResult) == typeof(Task<>).MakeGenericType(seekListType)
            || typeof(TResult) == seekListType;
    }

    private static SeekExpression? GetLookAheadSeek(Expression source, object? origin)
    {
        if (FindSeekVisitor.Instance.Visit(source) is not SeekExpression seek)
        {
            return null;
        }

        return seek.Update(
            seek.Query,
            Expression.Constant(origin),
            seek.Direction,
            seek.Size + 1);
    }

    private IQueryable CreateItemsQuery(Expression expression, SeekExpression? seek)
    {
        if (seek is null)
        {
            return CreateQuery(expression);
        }

        var insertExpansion = new InsertExpandedSeekVisitor(_source, seek);

        var expanded = insertExpansion.Visit(expression);

        return CreateQuery(expanded);
    }

    private static SeekList<TEntity> CreateSeekList<TEntity>(List<TEntity> items, SeekExpression? query)
        where TEntity : class
    {
        if (query is null)
        {
            return new SeekList<TEntity>(
                items,
                SeekDirection.Forward,
                hasOrigin: false,
                lookAhead: false,
                targetSize: null);
        }

        var direction = query.Direction;
        bool hasOrigin = query.Origin.Value is not null;

        bool lookAhead = items.Count == query.Size;
        int? targetSize = query.Size - 1;

        // potential issue with extra items tracked that are not actually returned
        // keep eye on

        if (lookAhead && query.Direction is SeekDirection.Forward)
        {
            items.RemoveAt(items.Count - 1);
        }
        else if (lookAhead && query.Direction is SeekDirection.Backwards)
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
