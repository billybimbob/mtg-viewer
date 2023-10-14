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

namespace EntityFrameworkCore.Paging.Query;

internal sealed class SeekProvider : IAsyncQueryProvider
{
    private readonly IAsyncQueryProvider _source;
    private readonly LookAheadVisitor _lookAhead;
    private readonly ParseSeekVisitor _seekParser;
    private readonly QueryOriginVisitor _originQuery;
    private readonly TranslateSeekVisitor _seekTranslator;

    public SeekProvider(IAsyncQueryProvider source)
    {
        var evaluateMember = new EvaluateMemberVisitor();

        _source = source;
        _lookAhead = new LookAheadVisitor();
        _seekParser = new ParseSeekVisitor();
        _originQuery = new QueryOriginVisitor(source, evaluateMember);
        _seekTranslator = new TranslateSeekVisitor(source, evaluateMember);
    }

    #region Create Query

    public IQueryable CreateQuery(Expression expression)
        => SeekQuery.Create(this, expression);

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        => new SeekQuery<TElement>(this, expression);

    #endregion

    // keep eye on, weakly typed methods have not been tested
    // maybe just throw InvalidOperation instead

    #region Execute

    public object? Execute(Expression expression)
    {
        if (ExpressionHelpers.FindSeekListEntity(expression) is Type seekListEntity)
        {
            return Invoke(
                ExecuteSeekListMethod
                    .MakeGenericMethod(seekListEntity),
                expression);
        }

        return _source.Execute(TranslateSeekBy(expression));
    }

    public TResult Execute<TResult>(Expression expression)
    {
        if (ExpressionHelpers.IsToSeekList(expression))
        {
            return Invoke<TResult>(
                ExecuteSeekListMethod
                    .MakeGenericMethod(typeof(TResult).GenericTypeArguments[0]),
                expression);
        }

        return _source.Execute<TResult>(TranslateSeekBy(expression));
    }

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
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

    private async IAsyncEnumerable<TEntity> ExecuteAsyncEnumerable<TEntity>(Expression expression, [EnumeratorCancellation] CancellationToken cancel)
    {
        var seekByExpression = await TranslateSeekByAsync(expression, cancel).ConfigureAwait(false);

        var query = _source
            .CreateQuery<TEntity>(seekByExpression)
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
        var seekByExpression = await TranslateSeekByAsync(expression, cancel).ConfigureAwait(false);

        return await _source
            .ExecuteAsync<Task<T>>(seekByExpression, cancel)
            .ConfigureAwait(false);
    }

    #region Execute Seek List

    private SeekList<TEntity> ExecuteSeekList<TEntity>(Expression expression)
        where TEntity : class
    {
        var seekListExpression = TranslateSeekList(expression);

        var items = _source
            .CreateQuery<TEntity>(seekListExpression.Source)
            .ToList();

        return CreateSeekList(items, seekListExpression.Parameters);
    }

    private async Task<SeekList<TEntity>> ExecuteSeekListAsync<TEntity>(Expression expression, CancellationToken cancel)
        where TEntity : class
    {
        var seekListExpression = await TranslateSeekListAsync(expression, cancel).ConfigureAwait(false);

        var items = await _source
            .CreateQuery<TEntity>(seekListExpression.Source)
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        return CreateSeekList(items, seekListExpression.Parameters);
    }

    private static SeekList<TEntity> CreateSeekList<TEntity>(List<TEntity> items, SeekQueryExpression parameters)
        where TEntity : class
    {
        var direction = parameters.Direction;
        bool hasOrigin = parameters.Origin.Value is not null;

        bool lookAhead = items.Count == parameters.Size;
        int? targetSize = parameters.Size - 1;

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

    #region Translate Seek Expressions

    private Expression TranslateSeekBy(Expression expression)
    {
        object? origin = ExecuteOrigin(expression);

        var changedOrigin = ChangeOrigin(expression, origin);

        return _seekTranslator.Visit(changedOrigin);
    }

    private async Task<Expression> TranslateSeekByAsync(Expression expression, CancellationToken cancel)
    {
        object? origin = await ExecuteOriginAsync(expression, cancel).ConfigureAwait(false);

        var changedOrigin = ChangeOrigin(expression, origin);

        return _seekTranslator.Visit(changedOrigin);
    }

    private SeekListExpression TranslateSeekList(Expression expression)
    {
        object? origin = ExecuteOrigin(expression);

        var changedOrigin = ChangeOrigin(expression, origin);
        var changedSeekList = _lookAhead.Visit(changedOrigin);

        var source = _seekTranslator.Visit(changedSeekList);
        var parameters = _seekParser.Parse(changedSeekList);

        return new SeekListExpression(source, parameters);
    }

    private async Task<SeekListExpression> TranslateSeekListAsync(Expression expression, CancellationToken cancel)
    {
        object? origin = await ExecuteOriginAsync(expression, cancel).ConfigureAwait(false);

        var changedOrigin = ChangeOrigin(expression, origin);
        var changedSeekList = _lookAhead.Visit(changedOrigin);

        var source = _seekTranslator.Visit(changedSeekList);
        var parameters = _seekParser.Parse(changedSeekList);

        return new SeekListExpression(source, parameters);
    }

    private static Expression ChangeOrigin(Expression expression, object? origin)
    {
        var changeOrigin = new ChangeOriginVisitor(origin);
        return changeOrigin.Visit(expression);
    }

    #endregion

    #region Execute Origin

    private object? ExecuteOrigin(Expression source)
    {
        var originExpression = _originQuery.Visit(source);

        if (originExpression is ConstantExpression { Value: var origin })
        {
            return origin;
        }

        return _source
            .CreateQuery(originExpression)
            .FirstOrDefault();
    }

    private async Task<object?> ExecuteOriginAsync(Expression source, CancellationToken cancel)
    {
        var originExpression = _originQuery.Visit(source);

        if (originExpression is ConstantExpression { Value: var origin })
        {
            return origin;
        }

        return await _source
            .CreateQuery(originExpression)
            .FirstOrDefaultAsync(cancel)
            .ConfigureAwait(false);
    }

    #endregion

    private object? Invoke(MethodInfo method, params object[] parameters)
        => method.Invoke(this, parameters);

    private TResult Invoke<TResult>(MethodInfo method, params object[] parameters)
        => (TResult)method.Invoke(this, parameters)!;

    #region Method Infos

    private static readonly MethodInfo ExecuteAsyncEnumerableMethod
        = GetPrivateMethod(nameof(ExecuteAsyncEnumerable), typeof(Expression), typeof(CancellationToken));

    private static readonly MethodInfo ExecuteTaskAsyncMethod
        = GetPrivateMethod(nameof(ExecuteTaskAsync), typeof(Expression), typeof(CancellationToken));

    private static readonly MethodInfo ExecuteSeekListMethod
        = GetPrivateMethod(nameof(ExecuteSeekList), typeof(Expression));

    private static readonly MethodInfo ExecuteSeekListAsyncMethod
        = GetPrivateMethod(nameof(ExecuteSeekListAsync), typeof(Expression), typeof(CancellationToken));

    private static MethodInfo GetPrivateMethod(string name, params Type[] parameterTypes)
    {
        const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        return typeof(SeekProvider).GetMethod(name, privateInstance, parameterTypes)!;
    }

    #endregion
}
