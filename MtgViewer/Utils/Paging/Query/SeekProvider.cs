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
            seek.Update(newQuery.Expression, seek.Origin, seek.Direction, seek.Take),
            newQuery.ElementType);
    }

    public object? Execute(Expression expression)
    {
        var origin = GetOrigin(expression);

        var query = SeekQuery(expression, origin);

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

        // if (expression is not MethodCallExpression call
        //     || FindSeekVisitor.Instance.Visit(expression) is not SeekExpression seek)
        // {
        //     return CreateSeekQuery<TElement>(expression);
        // }

        // if (call.Method.IsGenericMethod is false
        //     || call.Method.GetGenericMethodDefinition() != QueryableMethods.Take
        //     || call.Arguments.ElementAtOrDefault(0) is not MethodCallExpression parent
        //     || call.Arguments.ElementAtOrDefault(1) is not ConstantExpression { Value: int count })
        // {
        //     return CreateSeekQuery<TElement>(call, seek);
        // }

        // return CreateSeekQuery<TElement>(
        //     parent, seek.Update(seek.Origin.Value, seek.Direction, count));
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

        var newSeek = seek.Update(newQuery.Expression, seek.Origin, seek.Direction, seek.Take);

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

        var origin = GetOrigin(expression);

        var query = SeekQuery(expression, origin);

        return _source.Execute<TResult>(query.Expression);
    }

    private static bool IsSeekListExecute<TResult>(Expression expression)
        => expression is MethodCallExpression call

            && call.Method
                == PagingExtensions
                    .ToSeekListMethodInfo
                    .MakeGenericMethod(typeof(TEntity))

            && typeof(TResult)
                == typeof(Task<>)
                    .MakeGenericType(typeof(SeekList<>)
                        .MakeGenericType(typeof(TEntity)));

    private SeekList<TEntity> ExecuteSeekList(Expression expression)
    {
        if (FindSeekVisitor.Instance.Visit(expression) is not SeekExpression seekExpression)
        {
            return ExecuteMissingSeekList(expression);
        }

        var addLookAhead = seekExpression.Update(
            expression,
            seekExpression.Origin,
            seekExpression.Direction,
            seekExpression.Take + 1);

        var items = CreateQuery<TEntity>(addLookAhead).ToList();

        bool hasOrigin = seekExpression.Origin.Value is not null;

        bool lookAhead = items.Count == seekExpression.Take + 1;

        if (lookAhead)
        {
            // potential issue with extra items tracked that are not actually returned
            // keep eye on

            items.RemoveAt(items.Count - 1);
        }

        var seek = new Seek<TEntity>(
            items, seekExpression.Direction, hasOrigin, seekExpression.Take, lookAhead);

        return new SeekList<TEntity>(seek, items);
    }

    private SeekList<TEntity> ExecuteMissingSeekList(Expression expression)
    {
        var items = CreateQuery<TEntity>(expression).ToList();

        var seek = new Seek<TEntity>(
            items,
            SeekDirection.Forward,
            hasOrigin: false,
            targetSize: null,
            lookAhead: false);

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
        if (FindSeekVisitor.Instance.Visit(expression) is not SeekExpression seekExpression)
        {
            return await ExecuteMissingSeekListAsync(expression, cancel);
        }

        var addLookAhead = seekExpression.Update(
            seekExpression.Query,
            seekExpression.Origin,
            seekExpression.Direction,
            seekExpression.Take + 1);

        var items = await CreateQuery<TEntity>(addLookAhead)
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        bool hasOrigin = seekExpression.Origin.Value is not null;

        bool lookAhead = items.Count == seekExpression.Take + 1;

        if (lookAhead)
        {
            // potential issue with extra items tracked that are not actually returned
            // keep eye on

            items.RemoveAt(items.Count - 1);
        }

        var seek = new Seek<TEntity>(
            items, seekExpression.Direction, hasOrigin, seekExpression.Take, lookAhead);

        return new SeekList<TEntity>(seek, items);
    }

    private async Task<SeekList<TEntity>> ExecuteMissingSeekListAsync(
        Expression expression,
        CancellationToken cancel)
    {
        var items = await CreateQuery<TEntity>(expression)
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        var seek = new Seek<TEntity>(
            items,
            SeekDirection.Forward,
            hasOrigin: false,
            targetSize: null,
            lookAhead: false);

        return new SeekList<TEntity>(seek, items);
    }

    private async IAsyncEnumerable<TEntity> ExecuteAsyncEnumerable(
        Expression expression, [EnumeratorCancellation] CancellationToken cancel)
    {
        var origin = await GetOriginAsync(expression, cancel).ConfigureAwait(false);

        var query = SeekQuery(expression, origin)
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
        var origin = await GetOriginAsync(expression, cancel)
            .ConfigureAwait(false);

        var query = SeekQuery(expression, origin);

        return await _source.ExecuteAsync<Task<T>>(query.Expression, cancel)
            .ConfigureAwait(false);
    }

    #endregion

    #region Fetch Origin

    private TEntity? GetOrigin(Expression expression)
    {
        if (FindSeekVisitor.Instance.Visit(expression) is not SeekExpression seek
            || FindRootQuery.Instance.Visit(expression) is not QueryRootExpression root)
        {
            return default;
        }

        if (seek.Origin.Value is TEntity keyEntity)
        {
            return keyEntity;
        }

        if (FindSelector(expression, root) is LambdaExpression selector)
        {
            return GetOrigin(seek, root, selector);
        }

        if (seek.Origin.Value is not object key)
        {
            return default;
        }

        return GetOriginQuery(expression, root, key)
            .SingleOrDefault();
    }

    private async Task<TEntity?> GetOriginAsync(Expression expression, CancellationToken cancel)
    {
        if (FindSeekVisitor.Instance.Visit(expression) is not SeekExpression seek
            || FindRootQuery.Instance.Visit(expression) is not QueryRootExpression root)
        {
            return default;
        }

        if (seek.Origin.Value is TEntity keyEntity)
        {
            return keyEntity;
        }

        if (FindSelector(expression, root) is LambdaExpression selector)
        {
            return await GetOriginAsync(seek, root, selector, cancel);
        }

        if (seek.Origin.Value is not object key)
        {
            return default;
        }

        return await GetOriginQuery(expression, root, key)
            .SingleOrDefaultAsync(cancel)
            .ConfigureAwait(false);
    }

    private static LambdaExpression? FindSelector(
        Expression expression,
        QueryRootExpression root)
    {
        if (root.EntityType.ClrType == typeof(TEntity))
        {
            return default;
        }

        var findSelect = new FindSelect(root.EntityType.ClrType, typeof(TEntity));

        return findSelect.Visit(expression) as LambdaExpression;
    }

    private TEntity? GetOrigin(
        SeekExpression seek,
        QueryRootExpression root,
        LambdaExpression selector)
    {
        if (seek.Origin.Type == root.EntityType.ClrType)
        {
            return (TEntity?)selector
                .Compile()
                .DynamicInvoke(seek.Origin.Value);
        }

        if (seek.Origin.Value is not object key)
        {
            return default;
        }

        return GetOriginQuery(root, selector, key)
            .SingleOrDefault();
    }

    private async Task<TEntity?> GetOriginAsync(
        SeekExpression seek,
        QueryRootExpression root,
        LambdaExpression selector,
        CancellationToken cancel)
    {
        if (seek.Origin.Type == root.EntityType.ClrType)
        {
            return (TEntity?)selector
                .Compile()
                .DynamicInvoke(seek.Origin.Value);
        }

        if (seek.Origin.Value is not object key)
        {
            return default;
        }

        return await GetOriginQuery(root, selector, key)
            .SingleOrDefaultAsync(cancel)
            .ConfigureAwait(false);
    }

    private IQueryable<TEntity> GetOriginQuery(
        Expression expression,
        QueryRootExpression root,
        object key)
    {
        if (typeof(TEntity) != root.EntityType.ClrType)
        {
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} does not expected type {root.EntityType.ClrType.Name}");
        }

        var findByKey = new FindByKeyVisitor(root.EntityType);

        var query = _source
            .CreateQuery<TEntity>(findByKey
                .Visit(expression));

        foreach (string include in findByKey.Include)
        {
            query = query.Include(include);
        }

        var entityParameter = Expression
            .Parameter(
                type: query.ElementType,
                name: query.ElementType.Name[0].ToString().ToLowerInvariant());

        var keyProperty = Expression
            .Property(entityParameter, GetKeyProperty(root, key));

        var orderByKey = Expression
            .Call(
                instance: null,
                method: QueryableMethods.OrderBy
                    .MakeGenericMethod(query.ElementType, keyProperty.Type),
                arg0: query.Expression,
                arg1: Expression
                    .Quote(Expression
                        .Lambda(keyProperty, entityParameter)));

        var equalsToKey = Expression
            .Lambda<Func<TEntity, bool>>(Expression
                .Equal(
                    keyProperty,
                    Expression.Constant(key, keyProperty.Type)),
                entityParameter);

        return query.Provider
            .CreateQuery<TEntity>(orderByKey)
            .Where(equalsToKey)
            .AsNoTracking();
    }

    private IQueryable<TEntity> GetOriginQuery(
        QueryRootExpression root,
        LambdaExpression selector,
        object key)
    {
        var sourceParameter = Expression
            .Parameter(
                type: root.EntityType.ClrType,
                name: root.EntityType.ClrType.Name[0].ToString().ToLowerInvariant());

        var keyProperty = Expression
            .Property(sourceParameter, GetKeyProperty(root, key));

        var equalsToKey = Expression
            .Lambda(Expression
                .Equal(
                    keyProperty,
                    Expression.Constant(key, keyProperty.Type)),
                sourceParameter);

        var query = _source
            .CreateQuery(Expression
                .Call(
                    instance: null,
                    method: QueryableMethods.Where
                        .MakeGenericMethod(root.EntityType.ClrType),
                    arg0: root,
                    arg1: Expression.Quote(equalsToKey)));

        query = query.Provider
            .CreateQuery(Expression
                .Call(
                    instance: null,
                    method: QueryableMethods.OrderBy
                        .MakeGenericMethod(query.ElementType, keyProperty.Type),
                    arg0: query.Expression,
                    arg1: Expression
                        .Quote(Expression
                            .Lambda(keyProperty, sourceParameter))));

        return query.Provider
            .CreateQuery<TEntity>(Expression
                .Call(
                    instance: null,
                    method: QueryableMethods.Select
                        .MakeGenericMethod(query.ElementType, typeof(TEntity)),
                    arg0: query.Expression,
                    arg1: Expression.Quote(selector)));
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

    #endregion

    private IQueryable<TEntity> SeekQuery(Expression expression, TEntity? origin)
    {
        var expander = new ExpandSeekVisitor<TEntity>(_source, origin);

        var expandedExpression = expander.Visit(expression);

        return _source.CreateQuery<TEntity>(expandedExpression);
    }
}
