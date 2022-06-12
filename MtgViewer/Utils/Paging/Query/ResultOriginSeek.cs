using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class ResultOriginSeek<TSource, TResult, TRefKey, TValueKey> : ISeekable<TResult>
    where TSource : class
    where TResult : class
    where TRefKey : class
    where TValueKey : struct
{
    private readonly IQueryable<TResult> _query;
    private readonly SeekDirection _direction;
    private readonly int? _take;

    private readonly TRefKey? _referenceKey;
    private readonly TValueKey? _valueKey;

    internal ResultOriginSeek(
        IQueryable<TResult> query,
        SeekDirection direction,
        int? take,
        TRefKey? refKey,
        TValueKey? valueKey)
    {
        ArgumentNullException.ThrowIfNull(query);

        _query = query;
        _take = take;
        _direction = direction;

        _referenceKey = refKey;
        _valueKey = valueKey;
    }

    public ISeekable<TResult> OrderBy<TNewSource>()
        where TNewSource : class
    {
        if (typeof(TNewSource) == typeof(TSource))
        {
            return this;
        }

        return new ResultOriginSeek<TNewSource, TResult, TRefKey, TValueKey>(
            _query, _direction, _take, _referenceKey, _valueKey);
    }

    public ISeekable<TResult> Take(int count)
    {
        if (count == _take)
        {
            return this;
        }

        return new ResultOriginSeek<TSource, TResult, TRefKey, TValueKey>(
            _query, _direction, count, _referenceKey, _valueKey);
    }

    public async Task<SeekList<TResult>> ToSeekListAsync(CancellationToken cancel = default)
    {
        var origin = await GetOriginAsync(cancel).ConfigureAwait(false);

        return await SeekQuery(origin)
            .AsSeekQueryable()
            .ToSeekListAsync(cancel)
            .ConfigureAwait(false);
    }

    private IQueryable<TResult> SeekQuery(TResult? origin)
    {
        var query = _query
            .WithSelect<TSource, TResult>()
            .SeekOrigin(origin, _direction);

        return _take is int count
            ? query.Take(count)
            : query;
    }

    private async Task<TResult?> GetOriginAsync(CancellationToken cancel)
    {
        if (_referenceKey is TResult keyOrigin)
        {
            return keyOrigin;
        }

        if (_valueKey is TValueKey valueKey)
        {
            return await GetOriginQuery(valueKey)
                .SingleOrDefaultAsync(cancel)
                .ConfigureAwait(false);
        }

        if (_referenceKey is TRefKey refKey)
        {
            return await GetOriginQuery(refKey)
                .SingleOrDefaultAsync(cancel)
                .ConfigureAwait(false);
        }

        return null;
    }

    private IQueryable<TResult> GetOriginQuery<TKey>(TKey key)
    {
        var entityParameter = Expression.Parameter(
            typeof(TSource), typeof(TSource).Name[0].ToString().ToLowerInvariant());

        var getKey = GetEntityType().GetKeyInfo<TKey>();

        var propertyId = Expression.Property(entityParameter, getKey);

        var orderId = Expression.Lambda<Func<TSource, TKey>>(
            propertyId,
            entityParameter);

        var equalId = Expression.Lambda<Func<TSource, bool>>(
            Expression.Equal(propertyId, Expression.Constant(key)),
            entityParameter);

        var findById = new FindByKeyVisitor();

        var selector = SelectQueries.GetSelectQuery<TSource, TResult>(_query).Selector;

        return _query.Provider
            .CreateQuery<TSource>(findById.Visit(_query.Expression))
            .Where(equalId)
            .OrderBy(orderId)
            .Select(selector);
    }

    private IEntityType GetEntityType()
    {
        if (FindRootQuery.Instance.Visit(_query.Expression)
            is not QueryRootExpression { EntityType: var entity })
        {
            throw new ArgumentException("Missing a query root", nameof(_query));
        }

        if (entity.ClrType == typeof(TSource))
        {
            return entity;
        }

        var navEntity = entity
            .GetNavigations()
            .Where(n => n.ClrType == typeof(TSource))
            .Select(n => n.TargetEntityType)
            .FirstOrDefault();

        if (navEntity is null)
        {
            throw new InvalidOperationException($"Type {typeof(TSource).Name} could not be found");
        }

        return navEntity;
    }
}
