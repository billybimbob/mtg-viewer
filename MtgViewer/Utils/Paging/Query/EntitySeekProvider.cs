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

internal sealed class EntitySeekProvider<TEntity> : IAsyncQueryProvider
{
    private readonly IAsyncQueryProvider _source;

    public EntitySeekProvider(IAsyncQueryProvider source)
    {
        _source = source;
    }

    public IQueryable CreateQuery(Expression expression)
        => throw new NotSupportedException("Only strongly typed queries can be made");

    public object? Execute(Expression expression)
        => throw new NotSupportedException("Only strongly typed queries can be made");

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        if (expression is MethodCallExpression call
            && FindSeekVisitor.Instance.Visit(expression) is SeekExpression seek)
        {
            return CreateTakeQuery<TElement>(call, seek)
                ?? CreateQuery<TElement>(call, seek);
        }

        var newProvider = GetNewQueryProvider<TElement>();

        var newExpression = _source.CreateQuery<TElement>(expression).Expression;

        return new SeekQuery<TElement>(newProvider, newExpression);
    }

    private IAsyncQueryProvider GetNewQueryProvider<TElement>()
        => typeof(TElement) != typeof(TEntity)
            ? new SelectSeekProvider<TEntity, TElement>(_source)
            : this;

    private IQueryable<TElement> CreateQuery<TElement>(MethodCallExpression call, SeekExpression seek)
    {
        var removedSeek = RemoveSeekVisitor.Instance.Visit(call);

        var newExpression = _source.CreateQuery<TElement>(removedSeek).Expression;

        var addNewSeek = new AddSeekVisitor(seek);

        var newProvider = GetNewQueryProvider<TElement>();

        return new SeekQuery<TElement>(newProvider, addNewSeek.Visit(newExpression));
    }

    private IQueryable<TElement>? CreateTakeQuery<TElement>(MethodCallExpression call, SeekExpression seek)
    {
        if (call.Method.IsGenericMethod is false
            || call.Method.GetGenericMethodDefinition() != QueryableMethods.Take)
        {
            return null;
        }

        if (call.Arguments.ElementAtOrDefault(0) is not MethodCallExpression parent)
        {
            return null;
        }

        if (call.Arguments.ElementAtOrDefault(1) is not ConstantExpression { Value: int count })
        {
            return null;
        }

        var newSeek = seek.Update(seek.Origin.Value, seek.Direction, count);

        return CreateQuery<TElement>(parent, newSeek);
    }

    // TODO: use expression visitor to find seek expression instead of type cast

    public TResult Execute<TResult>(Expression expression)
    {
        var origin = GetOrigin(expression);

        var query = SeekQuery(expression, origin);

        return _source.Execute<TResult>(query.Expression);
    }

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        if (typeof(TResult) == typeof(IAsyncEnumerable<>).MakeGenericType(typeof(TEntity)))
        {
            return (TResult)ExecuteAsyncEnumerable(expression, cancellationToken);
        }

        if (typeof(TResult).IsGenericType is false
            || typeof(TResult).GetGenericTypeDefinition() != typeof(Task<>))
        {
            throw new NotSupportedException($"Unexpected result type: {typeof(TResult).Name}");
        }

        return ExecuteDynamicAsync<TResult>(expression, cancellationToken);
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
        const string executeName = nameof(EntitySeekProvider<TEntity>.ExecuteTaskAsync);

        const BindingFlags privateFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        return (TResult)typeof(EntitySeekProvider<>)
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

    private TEntity? GetOrigin(Expression expression)
    {
        if (FindSeekVisitor.Instance.Visit(expression) is not SeekExpression seek)
        {
            return default;
        }

        if (seek.Origin.Value is TEntity keyEntity)
        {
            return keyEntity;
        }

        if (seek.Origin.Value is not object key)
        {
            return default;
        }

        return GetOriginQuery(seek.Root, selector, key).SingleOrDefault();
    }

    private async Task<TEntity?> GetOriginAsync(Expression expression, CancellationToken cancel)
    {
        if (FindSeekVisitor.Instance.Visit(expression) is not SeekExpression seek)
        {
            return default;
        }

        if (seek.Origin.Value is TEntity keyEntity)
        {
            return keyEntity;
        }

        if (seek.Origin.Value is not object key)
        {
            return default;
        }

        return await GetOriginQuery(seek.Root, selector, key)
            .SingleOrDefaultAsync(cancel)
            .ConfigureAwait(false);
    }

    private IQueryable<TEntity> GetOriginQuery(
        QueryRootExpression root,
        Expression<Func<TSource, TEntity>> selector,
        object key)
    {
        var getKey = root.GetEntityType<TSource>().GetKeyInfo();

        var keyType = Nullable.GetUnderlyingType(key.GetType()) ?? key.GetType();

        if (getKey.PropertyType != keyType)
        {
            throw new InvalidOperationException(
                $"Key type ({key.GetType().Name}) is not expected type {getKey.PropertyType.Name}");
        }

        var sourceParameter = Expression.Parameter(
            typeof(TSource), typeof(TSource).Name[0].ToString().ToLowerInvariant());

        var keyProperty = Expression.Property(sourceParameter, getKey);

        var keyLambda = Expression.Lambda(keyProperty, sourceParameter);

        var equalsToKey = Expression.Lambda<Func<TSource, bool>>(
            Expression.Equal(keyProperty, Expression.Constant(key, keyType)),
            sourceParameter);

        var orderByKey = Expression.Call(
            null, QueryableMethods.OrderBy, root, Expression.Quote(keyLambda));

        return _source
            .CreateQuery<TSource>(orderByKey)
            .Where(equalsToKey)
            .Select(selector);
    }

    private IQueryable<TEntity> SeekQuery(Expression expression, TEntity? origin)
    {
        var call = FindExecute(expression);

        if (call is not null)
        {
            expression = RemoveExecute(expression);
        }

        if (FindSeekVisitor.Instance.Visit(expression) is not SeekExpression seek)
        {
            throw new InvalidOperationException($"Cannot find {nameof(SeekExpression)}");
        }

        expression = RemoveSeekVisitor.Instance.Visit(expression);

        var query = ExpandSeek(expression, origin, seek);

        if (call is not null)
        {
            query = _source.CreateQuery<TEntity>(
                call.Update(
                    call.Object,
                    call.Arguments.Skip(1).Prepend(query.Expression)));
        }

        return query;
    }

    private static MethodCallExpression? FindExecute(Expression expression)
    {
        if (expression is MethodCallExpression call
            && ExpressionHelpers.IsExecuteMethod(call.Method)
            && call.Arguments.ElementAtOrDefault(0) is not null)
        {
            return call;
        }

        return null;
    }

    private static Expression RemoveExecute(Expression expression)
    {
        if (expression is MethodCallExpression call
            && ExpressionHelpers.IsExecuteMethod(call.Method)
            && call.Arguments.ElementAtOrDefault(0) is Expression parent)
        {
            return parent;
        }

        return expression;
    }

    private IQueryable<TEntity> ExpandSeek(Expression expression, TEntity? origin, SeekExpression seek)
    {
        var query = _source.CreateQuery<TEntity>(expression);

        var filter = OriginFilter.Build(query, origin, seek.Direction);

        return (seek.Direction, filter, seek.Take) switch
        {
            (SeekDirection.Forward, not null, int count) => query
                .Where(filter)
                .Take(count),

            (SeekDirection.Backwards, not null, int count) => query
                .Reverse()
                .Where(filter)
                .Take(count)
                .Reverse(),

            (SeekDirection.Forward, not null, null) => query
                .Where(filter),

            (SeekDirection.Backwards, not null, null) => query
                .Reverse()
                .Where(filter)
                .Reverse(),

            (SeekDirection.Forward, null, int count) => query
                .Take(count),

            (SeekDirection.Backwards, null, int count) => query
                .Reverse()
                .Take(count)
                .Reverse(),

            _ => query
        };
    }
}

