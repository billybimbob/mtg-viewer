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

    IQueryable IQueryProvider.CreateQuery(Expression expression)
        => SeekQuery.Create(this, expression);

    IQueryable<TElement> IQueryProvider.CreateQuery<TElement>(Expression expression)
        => new SeekQuery<TElement>(this, expression);

    #endregion

    // keep eye on, weakly typed methods have not been tested
    // maybe just throw InvalidOperation instead

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
        var changeOrigin = new ChangeSeekOriginVisitor(origin);

        var updatedOrigin = changeOrigin.Visit(expression);

        var seekTranslation = SeekTranslationVisitor.Instance.Visit(updatedOrigin);

        return ExpandSeek(seekTranslation);
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
        var changeOrigin = new ChangeSeekOriginVisitor(origin);

        var updatedOrigin = changeOrigin.Visit(expression);

        var seekTranslation = SeekTranslationVisitor.Instance.Visit(updatedOrigin);

        return LookAheadSeekVisitor.Instance.Visit(seekTranslation);
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

        if (query is ConstantExpression { Value: var origin })
        {
            return origin;
        }

        return CreateOriginQuery(query)
            .FirstOrDefault();
    }

    private async Task<object?> ExecuteOriginAsync(Expression source, CancellationToken cancel)
    {
        var query = FindOriginQuery(source);

        if (query is ConstantExpression { Value: var origin })
        {
            return origin;
        }

        return await CreateOriginQuery(query)
            .FirstOrDefaultAsync(cancel)
            .ConfigureAwait(false);
    }

    private Expression FindOriginQuery(Expression source)
    {
        if (FindOrderParameterVisitor.Instance.Visit(source)
            is not ParameterExpression orderBy)
        {
            throw new InvalidOperationException(
                "No valid Ordering could be found, be sure to not call \"OrderBy\" after \"SeekBy\"");
        }

        var findOriginQuery = new OriginTranslationVisitor(_source, orderBy);

        return findOriginQuery.Visit(source);
    }

    private IQueryable CreateOriginQuery(Expression expression)
    {
        var query = _source.CreateQuery(expression);

        if (IsSelectedQuery(query))
        {
            return query;
        }

        var root = FindQueryRoot(query.Expression);

        var includes = OriginIncludeVisitor.Scan(query.Expression, root.EntityType);

        foreach (string include in includes)
        {
            query = query.Include(include);
        }

        return query.AsNoTracking();
    }

    private static bool IsSelectedQuery(IQueryable query)
    {
        var findSelector = new FindSelectVisitor(query.ElementType);

        return findSelector.Visit(query.Expression) is LambdaExpression;
    }

    private static QueryRootExpression FindQueryRoot(Expression query)
    {
        if (FindQueryRootVisitor.Instance.Visit(query) is not QueryRootExpression root)
        {
            throw new InvalidOperationException(
                $"{nameof(query)} is missing parameter of type {nameof(QueryRootExpression)}");
        }

        return root;
    }

    #endregion
}
