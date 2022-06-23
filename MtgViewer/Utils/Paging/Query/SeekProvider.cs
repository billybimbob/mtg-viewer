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
        => new SeekQuery<TElement>(this, expression);

    #endregion

    #region Execute

    object? IQueryProvider.Execute(Expression expression)
    {
        object? origin = ExecuteOrigin(expression);

        var sourceExpression = TranslateToSource(expression, origin);

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

        var sourceExpression = TranslateToSource(expression, origin);

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

    private Expression TranslateToSource(Expression expression, object? origin)
    {
        var changeOrigin = new ChangeSeekOriginVisitor(Expression.Constant(origin));

        var seekTranslation = SeekTranslationVisitor.Instance.Visit(expression);

        var updatedOrigin = changeOrigin.Visit(seekTranslation);

        return ExpandSeek(updatedOrigin);
    }

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

        var sourceExpression = TranslateToSource(expression, origin);

        var query = _source
            .CreateQuery<T>(sourceExpression)
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

        var sourceExpression = TranslateToSource(expression, origin);

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

        var updatedSeek = TranslateToSeekList(expression, origin);

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

        var updatedSeek = TranslateToSeekList(expression, origin);

        var items = await _source
            .CreateQuery<TEntity>(ExpandSeek(updatedSeek))
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        var seek = FindSeekVisitor.Instance.Visit(updatedSeek) as SeekExpression;

        return CreateSeekList(items, seek);
    }

    private static Expression TranslateToSeekList(Expression expression, object? origin)
    {
        var changeOrigin = new ChangeSeekOriginVisitor(Expression.Constant(origin));

        var seekTranslation = SeekTranslationVisitor.Instance.Visit(expression);

        var updatedOrigin = changeOrigin.Visit(seekTranslation);

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
        var query = FindOriginQuery(source);

        return GetLocalOrigin(query)
            ?? CreateOriginQuery(query, source)
                ?.SingleOrDefault();
    }

    private async Task<object?> ExecuteOriginAsync(Expression source, CancellationToken cancel)
    {
        var query = FindOriginQuery(source);

        if (GetLocalOrigin(query) is object local)
        {
            return local;
        }

        if (CreateOriginQuery(query, source) is not IQueryable originQuery)
        {
            return null;
        }

        return await originQuery
            .SingleOrDefaultAsync(cancel)
            .ConfigureAwait(false);
    }

    private static OriginQueryExpression FindOriginQuery(Expression source)
    {
        if (FindOrderParameterVisitor.Instance.Visit(source)
            is not ParameterExpression orderBy)
        {
            throw new InvalidOperationException(
                "No valid Ordering could be found, be sure to not call \"OrderBy\" after \"SeekBy\"");
        }

        var findOriginQuery = new OriginQueryTranslationVisitor(orderBy);

        if (findOriginQuery.Visit(source) is not OriginQueryExpression query)
        {
            throw new InvalidOperationException(
                "Origin query can not be inferred from specified query");
        }

        return query;
    }

    private static object? GetLocalOrigin(OriginQueryExpression query)
    {
        if (query.Origin.Type == query.Type)
        {
            return query.Origin.Value;
        }

        if (query.Origin.Type != query.Selector?.Parameters.FirstOrDefault()?.Type)
        {
            return null;
        }

        return query.Selector
            .Compile()
            .DynamicInvoke(query.Origin.Value);
    }

    private IQueryable? CreateOriginQuery(OriginQueryExpression query, Expression source)
    {
        return query switch
        {
            { Origin.Value: not null, Key: not null, Selector: null }
                => CreateOriginQuery(query.Root, query.Origin, query.Key, source),

            { Origin.Value: not null, Key: not null }
                => CreateOriginQuery(query.Root, query.Origin, query.Key, query.Selector),

            _ => null
        };
    }

    private IQueryable CreateOriginQuery(
        QueryRootExpression root,
        ConstantExpression key,
        LambdaExpression keySelector,
        LambdaExpression selector)
    {
        var equalsToKey = Expression.Equal(
            KeyProperty(keySelector, key), UnwrapNullable(key));

        var originParameter = OriginParameter(keySelector, selector.Body.Type);

        return _source
            .CreateQuery(root)
            .Select(selector)
            .Where(Expression.Lambda(equalsToKey, originParameter))
            .OrderBy(keySelector);
    }

    private IQueryable CreateOriginQuery(
        QueryRootExpression root,
        ConstantExpression key,
        LambdaExpression keySelector,
        Expression source)
    {
        var includes = OriginIncludeVisitor.Scan(source, root.EntityType);

        var query = _source.CreateQuery(root);

        foreach (string include in includes)
        {
            query = query.Include(include);
        }

        var equalsToKey = Expression.Equal(
            KeyProperty(keySelector, key), UnwrapNullable(key));

        var originParameter = OriginParameter(keySelector, root.EntityType.ClrType);

        return query
            .Where(Expression.Lambda(equalsToKey, originParameter))
            .OrderBy(keySelector)
            .AsNoTracking();
    }

    private static MemberExpression KeyProperty(LambdaExpression keySelector, ConstantExpression key)
    {
        var propertyType = Nullable.GetUnderlyingType(key.Type) ?? key.Type;

        if (keySelector.Body is MemberExpression property && property.Type == propertyType)
        {
            return property;
        }

        throw new InvalidOperationException(
            $"The key from {keySelector} is not expected type {propertyType.Name}");
    }

    private static ConstantExpression UnwrapNullable(ConstantExpression key)
    {
        if (key.Value is null)
        {
            return key;
        }

        var innerType = Nullable.GetUnderlyingType(key.Type) ?? key.Type;

        if (innerType == key.Type)
        {
            return key;
        }

        return Expression.Constant(key.Value, innerType);
    }

    private static ParameterExpression OriginParameter(LambdaExpression keySelector, Type expectedType)
    {
        if (keySelector.Parameters.ElementAtOrDefault(0) is ParameterExpression parameter
            && parameter.Type == expectedType)
        {
            return parameter;
        }

        throw new InvalidOperationException(
            $"Parameter from {keySelector} is not expected type {expectedType.Name}");
    }

    #endregion
}
