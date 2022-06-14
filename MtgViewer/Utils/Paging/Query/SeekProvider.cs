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

using EntityFrameworkCore.Paging.Extensions;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class SeekProvider<TEntity> : IAsyncQueryProvider
    where TEntity : class
{
    private readonly IAsyncQueryProvider _source;

    public SeekProvider(IAsyncQueryProvider source)
    {
        _source = source;
    }

    #region Weakly Typed

    public IQueryable CreateQuery(Expression expression)
    {
        if (FindSeekVisitor.Instance.Visit(expression) is SeekExpression seek)
        {
            return CreateSeekQuery(expression, seek);
        }

        return CreateSeekQuery(expression);
    }

    private IQueryable CreateSeekQuery(Expression expression)
    {
        var newQuery = _source.CreateQuery(expression);

        return new SeekQuery(this, newQuery.Expression, newQuery.ElementType);
    }

    private IQueryable CreateSeekQuery(Expression expression, SeekExpression seek)
    {
        var removedSeek = RemoveSeekVisitor.Instance.Visit(expression);

        var newQuery = _source.CreateQuery(removedSeek);

        return new SeekQuery(
            this,
            seek.Update(newQuery.Expression, seek.Origin, seek.Direction, seek.Size),
            newQuery.ElementType);
    }

    public object? Execute(Expression expression)
    {
        object? origin = ExecuteOrigin(expression);

        var query = CreateEntityQuery(expression, origin);

        return _source.Execute(query.Expression);
    }

    #endregion

    #region Create Query (strongly typed)

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        if (FindSeekVisitor.Instance.Visit(expression) is SeekExpression seek)
        {
            return CreateSeekQuery<TElement>(expression, seek);
        }

        return CreateSeekQuery<TElement>(expression);
    }

    private IQueryable<TElement> CreateSeekQuery<TElement>(Expression expression)
    {
        var newQuery = _source.CreateQuery<TElement>(expression);

        if (typeof(TElement).IsValueType)
        {
            return newQuery;
        }

        var newProvider = GetNewQueryProvider<TElement>();

        return (IQueryable<TElement>)Activator
            .CreateInstance(
                typeof(SeekQuery<>).MakeGenericType(typeof(TElement)),
                new object[] { newProvider, newQuery.Expression })!;
    }

    private IQueryable<TElement> CreateSeekQuery<TElement>(Expression expression, SeekExpression seek)
    {
        var removedSeek = RemoveSeekVisitor.Instance.Visit(expression);

        var newQuery = _source.CreateQuery<TElement>(removedSeek);

        if (typeof(TElement).IsValueType)
        {
            return newQuery;
        }

        var newProvider = GetNewQueryProvider<TElement>();

        var newSeek = seek.Update(newQuery.Expression, seek.Origin, seek.Direction, seek.Size);

        return (IQueryable<TElement>)Activator
            .CreateInstance(
                typeof(SeekQuery<>).MakeGenericType(typeof(TElement)),
                new object[] { newProvider, newSeek })!;
    }

    private IAsyncQueryProvider GetNewQueryProvider<TElement>()
    {
        if (typeof(TElement) == typeof(TEntity) || typeof(TElement).IsValueType)
        {
            return this;
        }

        return (IAsyncQueryProvider)Activator
            .CreateInstance(
                typeof(SeekProvider<>).MakeGenericType(typeof(TElement)),
                new object[] { _source })!;
    }

    #endregion

    public TResult Execute<TResult>(Expression expression)
    {
        if (IsSeekListExecute<TResult>(expression))
        {
            return (TResult)(object)ExecuteSeekList(expression);
        }

        object? origin = ExecuteOrigin(expression);

        var query = CreateEntityQuery(expression, origin);

        return _source.Execute<TResult>(query.Expression);
    }

    private static bool IsSeekListExecute<TResult>(Expression expression)
    {
        if (expression is not MethodCallExpression call)
        {
            return false;
        }

        var toSeekListMethod = PagingExtensions.ToSeekListMethodInfo.MakeGenericMethod(typeof(TEntity));

        if (call.Method != toSeekListMethod)
        {
            return false;
        }

        var seekListType = typeof(SeekList<>).MakeGenericType(typeof(TEntity));

        return typeof(TResult) == typeof(Task<>).MakeGenericType(seekListType)
            || typeof(TResult) == seekListType;
    }

    private SeekList<TEntity> ExecuteSeekList(Expression expression)
    {
        var seek = FindSeekVisitor.Instance.Visit(expression) as SeekExpression;

        var items = CreateItemsQuery(expression, seek).ToList();

        return CreateSeekList(items, seek);
    }

    private IQueryable<TEntity> CreateItemsQuery(Expression expression, SeekExpression? seek)
    {
        if (seek is null)
        {
            return CreateQuery<TEntity>(expression);
        }

        var addLookAhead = seek.Update(
            seek.Query,
            seek.Origin,
            seek.Direction,
            seek.Size + 1);

        return CreateQuery<TEntity>(addLookAhead);
    }

    private static SeekList<TEntity> CreateSeekList(List<TEntity> items, SeekExpression? query)
    {
        if (query is null)
        {
            var missingSeek = new Seek<TEntity>(
                items,
                SeekDirection.Forward,
                hasOrigin: false,
                targetSize: null,
                lookAhead: false);

            return new SeekList<TEntity>(missingSeek, items);
        }

        bool hasOrigin = query.Origin.Value is not null;

        bool lookAhead = items.Count == query.Size + 1;

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

        var seek = new Seek<TEntity>(
            items, query.Direction, hasOrigin, query.Size, lookAhead);

        return new SeekList<TEntity>(seek, items);
    }

    #region Execute Async

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
    {
        if (IsSeekListExecute<TResult>(expression))
        {
            return (TResult)(object)ExecuteSeekListAsync(expression, cancellationToken);
        }

        if (typeof(TResult) == typeof(IAsyncEnumerable<>).MakeGenericType(typeof(TEntity)))
        {
            return (TResult)ExecuteAsyncEnumerable(expression, cancellationToken);
        }

        if (typeof(TResult).IsGenericType
            && typeof(TResult).GetGenericTypeDefinition() == typeof(Task<>))
        {
            return ExecuteDynamicAsync<TResult>(expression, cancellationToken);
        }

        throw new NotSupportedException($"Unexpected result type: {typeof(TResult).Name}");
    }

    private async Task<SeekList<TEntity>> ExecuteSeekListAsync(Expression expression, CancellationToken cancel)
    {
        var seek = FindSeekVisitor.Instance.Visit(expression) as SeekExpression;

        var items = await CreateItemsQuery(expression, seek)
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        return CreateSeekList(items, seek);
    }

    private async IAsyncEnumerable<TEntity> ExecuteAsyncEnumerable(
        Expression expression, [EnumeratorCancellation] CancellationToken cancel)
    {
        object? origin = await ExecuteOriginAsync(expression, cancel).ConfigureAwait(false);

        var query = CreateEntityQuery(expression, origin)
            .AsAsyncEnumerable()
            .WithCancellation(cancel)
            .ConfigureAwait(false);

        await foreach (var entity in query)
        {
            yield return entity;
        }
    }

    private TResult ExecuteDynamicAsync<TResult>(Expression expression, CancellationToken cancel)
    {
        const string executeName = nameof(SeekProvider<TEntity>.ExecuteTaskAsync);

        const BindingFlags privateFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        return (TResult)typeof(SeekProvider<>)
            .MakeGenericType(typeof(TEntity))

            .GetMethod(executeName, privateFlags, new[] { expression.GetType(), cancel.GetType() })!

            .MakeGenericMethod(typeof(TResult).GenericTypeArguments[0])

            .Invoke(this, new object[] { expression, cancel })!;
    }

    private async Task<T> ExecuteTaskAsync<T>(Expression expression, CancellationToken cancel)
    {
        object? origin = await ExecuteOriginAsync(expression, cancel)
            .ConfigureAwait(false);

        var query = CreateEntityQuery(expression, origin);

        return await _source.ExecuteAsync<Task<T>>(query.Expression, cancel)
            .ConfigureAwait(false);
    }

    #endregion

    private IQueryable<TEntity> CreateEntityQuery(Expression expression, object? origin)
    {
        var expander = new ExpandSeekVisitor(_source, Expression.Constant(origin));

        var expandedExpression = expander.Visit(expression);

        return _source.CreateQuery<TEntity>(expandedExpression);
    }

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
        if (root.EntityType.ClrType == typeof(TEntity))
        {
            return CreateEntityOriginQuery(root, key, source);
        }

        // add include statements as well

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
            .Where(Expression
                .Lambda(equalsToKey, originParameter))

            .OrderBy(Expression
                .Lambda(keyProperty, originParameter))

            .AsNoTracking();
    }

    private IQueryable<TEntity> CreateEntityOriginQuery(
        QueryRootExpression root,
        object key,
        Expression source)
    {
        var originInclude = new OriginIncludeVisitor(root.EntityType);

        var query = _source
            .CreateQuery<TEntity>(originInclude.Visit(source));

        foreach (string include in originInclude.Includes)
        {
            query = query.Include(include);
        }

        var entityParameter = Expression
            .Parameter(
                type: root.EntityType.ClrType,
                name: root.EntityType.ClrType.Name[0].ToString().ToLowerInvariant());

        var keyProperty = Expression
            .Property(entityParameter, GetKeyProperty(root, key));

        var equalsToKey = Expression
            .Equal(
                keyProperty,
                Expression.Constant(key, keyProperty.Type));

        return query
            .OrderBy(Expression
                .Lambda(keyProperty, entityParameter))

            .Cast<TEntity>()
            .Where(Expression
                .Lambda<Func<TEntity, bool>>(equalsToKey, entityParameter))

            .AsNoTracking();
    }

    #endregion
}
