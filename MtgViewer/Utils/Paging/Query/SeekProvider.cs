using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

using EntityFrameworkCore.Paging.Query.Infrastructure;
using EntityFrameworkCore.Paging.Utils;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class SeekProvider : IAsyncQueryProvider
{
    private readonly IAsyncQueryProvider _source;
    private readonly TranslateSeekVisitor _seekTranslator;
    private readonly QueryOriginVisitor _originQuery;

    public SeekProvider(IAsyncQueryProvider source)
    {
        _source = source;
        _seekTranslator = new TranslateSeekVisitor(source);
        _originQuery = new QueryOriginVisitor(source);
    }

    #region MethodInfos

    private static readonly MethodInfo ExecuteAsyncEnumerableMethod
        = GetPrivateMethod(nameof(ExecuteAsyncEnumerable), typeof(Expression), typeof(CancellationToken));

    private static readonly MethodInfo ExecuteTaskAsyncMethod
        = GetPrivateMethod(nameof(ExecuteTaskAsync), typeof(Expression), typeof(CancellationToken));

    private static readonly MethodInfo ExecuteSeekListAsyncMethod
        = GetPrivateMethod(nameof(ExecuteSeekListAsync), typeof(Expression), typeof(CancellationToken));

    private static readonly MethodInfo ExecuteSeekListMethod
        = GetPrivateMethod(nameof(ExecuteSeekList), typeof(Expression));

    #endregion

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

        var changedOrigin = ChangeOriginVisitor.Visit(expression, origin);

        return _source.Execute(_seekTranslator.Visit(changedOrigin));
    }

    TResult IQueryProvider.Execute<TResult>(Expression expression)
    {
        if (ExpressionHelpers.IsToSeekList(expression)
            && typeof(TResult) is { GenericTypeArguments: [Type seek] })
        {
            return Invoke<TResult>(
                ExecuteSeekListMethod.MakeGenericMethod(seek),
                expression);
        }

        object? origin = ExecuteOrigin(expression);

        var changedOrigin = ChangeOriginVisitor.Visit(expression, origin);

        return _source.Execute<TResult>(_seekTranslator.Visit(changedOrigin));
    }

    TResult IAsyncQueryProvider.ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
    {
        if (ExpressionHelpers.IsToSeekList(expression)
            && typeof(TResult) is { GenericTypeArguments: [{ GenericTypeArguments: [Type seek] }] })
        {
            return Invoke<TResult>(
                ExecuteSeekListAsyncMethod.MakeGenericMethod(seek),
                expression,
                cancellationToken);
        }

        if (typeof(TResult).IsGenericType
            && typeof(TResult).GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)
            && typeof(TResult) is { GenericTypeArguments: [Type item] })
        {
            return Invoke<TResult>(
                ExecuteAsyncEnumerableMethod.MakeGenericMethod(item),
                expression,
                cancellationToken);
        }

        if (typeof(TResult).IsGenericType
            && typeof(TResult).GetGenericTypeDefinition() == typeof(Task<>)
            && typeof(TResult) is { GenericTypeArguments: [Type result] })
        {
            return Invoke<TResult>(
                ExecuteTaskAsyncMethod.MakeGenericMethod(result),
                expression,
                cancellationToken);
        }

        throw new NotSupportedException($"Unexpected result type: {typeof(TResult).Name}");
    }

    #endregion

    private async IAsyncEnumerable<T> ExecuteAsyncEnumerable<T>(
        Expression expression, [EnumeratorCancellation] CancellationToken cancel)
    {
        object? origin = await ExecuteOriginAsync(expression, cancel)
            .ConfigureAwait(false);

        var changedOrigin = ChangeOriginVisitor.Visit(expression, origin);

        var query = _source
            .CreateQuery<T>(_seekTranslator.Visit(changedOrigin))
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

        var changedOrigin = ChangeOriginVisitor.Visit(expression, origin);

        return await _source
            .ExecuteAsync<Task<T>>(_seekTranslator.Visit(changedOrigin), cancel)
            .ConfigureAwait(false);
    }

    #region Fetch Seek List

    private SeekList<TEntity> ExecuteSeekList<TEntity>(Expression expression)
        where TEntity : class
    {
        object? origin = ExecuteOrigin(expression);

        var changedOrigin = ChangeOriginVisitor.Visit(expression, origin);

        var changedSeekList = LookAheadVisitor.Instance.Visit(changedOrigin);

        var items = _source
            .CreateQuery<TEntity>(_seekTranslator.Visit(changedSeekList))
            .ToList();

        var seek = ParseSeekVisitor.Instance.Visit(changedSeekList) as SeekExpression;

        return CreateSeekList(items, seek);
    }

    private async Task<SeekList<TEntity>> ExecuteSeekListAsync<TEntity>(Expression expression, CancellationToken cancel)
        where TEntity : class
    {
        object? origin = await ExecuteOriginAsync(expression, cancel)
            .ConfigureAwait(false);

        var changedOrigin = ChangeOriginVisitor.Visit(expression, origin);

        var changedSeekList = LookAheadVisitor.Instance.Visit(changedOrigin);

        var items = await _source
            .CreateQuery<TEntity>(_seekTranslator.Visit(changedSeekList))
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        var seek = ParseSeekVisitor.Instance.Visit(changedSeekList) as SeekExpression;

        return CreateSeekList(items, seek);
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

        if (lookAhead && direction is SeekDirection.Forward)
        {
            items.RemoveAt(items.Count - 1);
        }
        else if (lookAhead && direction is SeekDirection.Backwards)
        {
            items.RemoveAt(0);
        }

        return new SeekList<TEntity>(items, direction, hasOrigin, lookAhead, targetSize);
    }

    #endregion

    #region Fetch Origin

    private object? ExecuteOrigin(Expression source)
    {
        if (FindOrderingVisitor.Instance.Visit(source) is not ParameterExpression)
        {
            throw new InvalidOperationException(
                "No valid Ordering could be found, be sure to not call \"OrderBy\" after \"SeekBy\"");
        }

        var expression = _originQuery.Visit(source);

        if (expression is ConstantExpression { Value: var origin })
        {
            return origin;
        }

        return _source
            .CreateQuery(expression)
            .FirstOrDefault();
    }

    private async Task<object?> ExecuteOriginAsync(Expression source, CancellationToken cancel)
    {
        if (FindOrderingVisitor.Instance.Visit(source) is not ParameterExpression)
        {
            throw new InvalidOperationException(
                "No valid Ordering could be found, be sure to not call \"OrderBy\" after \"SeekBy\"");
        }

        var expression = _originQuery.Visit(source);

        if (expression is ConstantExpression { Value: var origin })
        {
            return origin;
        }

        return await _source
            .CreateQuery(expression)
            .FirstOrDefaultAsync(cancel)
            .ConfigureAwait(false);
    }

    #endregion

    private TResult Invoke<TResult>(MethodInfo method, params object[] parameters)
        => (TResult)method.Invoke(this, parameters)!;

    private static MethodInfo GetPrivateMethod(string name, params Type[] parameterTypes)
    {
        const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        return typeof(SeekProvider)
            .GetMethod(name, privateInstance, parameterTypes)!;
    }
}
