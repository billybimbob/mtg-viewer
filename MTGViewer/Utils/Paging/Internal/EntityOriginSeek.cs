using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

internal sealed class EntityOriginSeek<TEntity, TRefKey, TValueKey> : ISeekable<TEntity>
    where TEntity : class
    where TRefKey : class
    where TValueKey : struct
{
    private readonly IQueryable<TEntity> _query;
    private readonly SeekDirection _direction;
    private readonly int? _take;

    private readonly TRefKey? _referenceKey;
    private readonly TValueKey? _valueKey;

    internal EntityOriginSeek(
        IQueryable<TEntity> query,
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

    public ISeekable<TEntity> OrderBy<TSource>()
        where TSource : class
    {
        if (typeof(TSource) == typeof(TEntity))
        {
            return this;
        }

        return new ResultOriginSeek<TSource, TEntity, TRefKey, TValueKey>(
            _query, _direction, _take, _referenceKey, _valueKey);
    }

    public ISeekable<TEntity> Take(int count)
    {
        if (count == _take)
        {
            return this;
        }

        return new EntityOriginSeek<TEntity, TRefKey, TValueKey>(
            _query, _direction, count, _referenceKey, _valueKey);
    }

    public async Task<SeekList<TEntity>> ToSeekListAsync(CancellationToken cancel = default)
    {
        var origin = await GetOriginAsync(cancel).ConfigureAwait(false);

        return await SeekQuery(origin)
            .ToSeekListAsync(cancel)
            .ConfigureAwait(false);
    }

    private IQueryable<TEntity> SeekQuery(TEntity? origin)
    {
        var query = _query.SeekOrigin(origin, _direction);

        return _take is int count
            ? query.Take(count)
            : query;
    }

    private async Task<TEntity?> GetOriginAsync(CancellationToken cancel)
    {
        if (_referenceKey is TEntity keyOrigin)
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

    private IQueryable<TEntity> GetOriginQuery<TKey>(TKey key)
    {
        if (FindRootQuery.Instance.Visit(_query.Expression)
            is not QueryRootExpression { EntityType: var entityType })
        {
            throw new InvalidOperationException("Cannot find a query root");
        }

        return entityType.ClrType == typeof(TEntity)
            ? GetEntityQuery(key, entityType)
            : GetSelectedQuery(key, entityType);
    }

    private IQueryable<TEntity> GetEntityQuery<TKey>(TKey key, IEntityType entityType)
    {
        var findById = new FindByIdVisitor(entityType);

        var originQuery = _query.Provider
            .CreateQuery<TEntity>(findById.Visit(_query.Expression));

        foreach (var include in findById.Include)
        {
            originQuery = originQuery.Include(include);
        }

        var entityParameter = Expression
            .Parameter(
                typeof(TEntity),
                typeof(TEntity).Name[0].ToString().ToLowerInvariant());

        var propertyId = Expression
            .Property(entityParameter, entityType.GetKeyInfo<TKey>());

        var equalId = Expression
            .Lambda<Func<TEntity, bool>>(Expression
                .Equal(propertyId, Expression.Constant(key)), entityParameter);

        var orderId = Expression
            .Lambda<Func<TEntity, TKey>>(propertyId, entityParameter);

        return originQuery
            .Where(equalId)
            .OrderBy(orderId)
            .AsNoTracking();
    }

    private IQueryable<TEntity> GetSelectedQuery<TKey>(TKey key, IEntityType entityType)
    {
        var (query, selector) = SelectQueries.GetSelectQuery(entityType.ClrType, _query);

        var findById = new FindByIdVisitor();

        var findQuery = query.Provider
            .CreateQuery(findById.Visit(query.Expression));

        var entityParameter = Expression
            .Parameter(
                entityType.ClrType, entityType.ClrType.Name[0].ToString().ToLowerInvariant());

        var propertyId = Expression
            .Property(entityParameter, entityType.GetKeyInfo<TKey>());

        var equalId = Expression
            .Quote(Expression
                .Lambda(Expression
                    .Equal(propertyId, Expression.Constant(key)), entityParameter));

        findQuery = findQuery.Provider
            .CreateQuery(Expression
                .Call(null,
                    QueryableMethods.Where.MakeGenericMethod(entityType.ClrType),
                    findQuery.Expression,
                    equalId));

        var orderId = Expression
            .Quote(Expression
                .Lambda(propertyId, entityParameter));

        findQuery = findQuery.Provider
            .CreateQuery(Expression
                .Call(null,
                    QueryableMethods.OrderBy.MakeGenericMethod(entityType.ClrType, typeof(TKey)),
                    findQuery.Expression,
                    orderId));

        return findQuery.Provider
            .CreateQuery<TEntity>(Expression
                .Call(null,
                    QueryableMethods.Select.MakeGenericMethod(entityType.ClrType, typeof(TEntity)),
                    findQuery.Expression,
                    Expression.Quote(selector)));
    }
}
