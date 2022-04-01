using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

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

    private IEntityType? _entityType;
    private IEntityType EntityType =>
        _entityType ??= SeekHelpers.GetEntityType<TEntity>(_query.Expression);


    private FindByIdVisitor? _findById;
    private FindByIdVisitor FindById => _findById ??= new(EntityType);


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


    // public ISeekable<TEntity> UseSourceOrigin()
    // {
    //     return new SourceOriginSeek<TEntity, TEntity, TRefKey, TValueKey>(
    //         _query, _direction, _take, _referenceKey, _valueKey);
    // }


    public ISeekable<TEntity> Take(int count)
    {
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
        var entityParameter = Expression.Parameter(
            typeof(TEntity), typeof(TEntity).Name[0].ToString().ToLower());

        var getKey = SeekHelpers.GetKeyInfo<TKey>(EntityType);

        var propertyId = Expression.Property(entityParameter, getKey);

        var orderId = Expression.Lambda<Func<TEntity, TKey>>(
            propertyId,
            entityParameter);

        var equalId = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(propertyId, Expression.Constant(key)),
            entityParameter);

        var originQuery = _query.Provider
            .CreateQuery<TEntity>(FindById.Visit(_query.Expression))
            .Where(equalId)
            .OrderBy(orderId)
            .AsNoTracking();

        foreach (var include in FindById.Include)
        {
            originQuery = originQuery.Include(include);
        }

        return originQuery;
    }
}



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

    private IEntityType? _entityType;
    private IEntityType EntityType =>
        _entityType ??= SeekHelpers.GetEntityType<TSource>(_query.Expression);


    private FindByIdVisitor? _findById;
    private FindByIdVisitor FindById => _findById ??= new(null);


    private SelectResult<TSource, TResult>? _result;
    private SelectResult<TSource, TResult> SelectResult =>
        _result ??= SelectQueries.GetSelectQuery<TSource, TResult>(_query);



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


    // public ISeekable<TResult> UseSourceOrigin()
    // {
    //     return new SourceOriginSeek<TSource, TResult, TRefKey, TValueKey>(
    //         _query, _direction, _take, _referenceKey, _valueKey);
    // }


    public ISeekable<TResult> Take(int count)
    {
        return new ResultOriginSeek<TSource, TResult, TRefKey, TValueKey>(
            _query, _direction, count, _referenceKey, _valueKey);
    }


    public async Task<SeekList<TResult>> ToSeekListAsync(CancellationToken cancel = default)
    {
        var origin = await GetOriginAsync(cancel).ConfigureAwait(false);

        return await SeekQuery(origin)
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
            typeof(TSource), typeof(TSource).Name[0].ToString().ToLower());

        var getKey = SeekHelpers.GetKeyInfo<TKey>(EntityType);

        var propertyId = Expression.Property(entityParameter, getKey);

        var orderId = Expression.Lambda<Func<TSource, TKey>>(
            propertyId,
            entityParameter);

        var equalId = Expression.Lambda<Func<TSource, bool>>(
            Expression.Equal(propertyId, Expression.Constant(key)),
            entityParameter);

        return _query.Provider
            .CreateQuery<TSource>(FindById.Visit(_query.Expression))
            .Where(equalId)
            .OrderBy(orderId)
            .Select(SelectResult.Selector);
    }

}



// internal sealed class SourceOriginSeek<TSource, TResult, TRefKey, TValueKey> : ISeekable<TResult>
//     where TSource : class
//     where TResult : class
//     where TRefKey : class
//     where TValueKey : struct
// {   
//     private readonly IQueryable<TResult> _query;
//     private readonly SeekDirection _direction;
//     private readonly int? _take;

//     private readonly TRefKey? _referenceKey;
//     private readonly TValueKey? _valueKey;

//     internal SourceOriginSeek(
//         IQueryable<TResult> query,
//         SeekDirection direction,
//         int? take,
//         TRefKey? refKey,
//         TValueKey? valueKey)
//     {
//         ArgumentNullException.ThrowIfNull(query);

//         _query = query;
//         _take = take;
//         _direction = direction;

//         _referenceKey = refKey;
//         _valueKey = valueKey;
//     }


//     private QueryRootExpression? _root;
//     private QueryRootExpression Root =>
//         _root ??= SeekHelpers.GetRoot<TSource>(_query.Expression);


//     private FindByIdVisitor? _findById;
//     private FindByIdVisitor FindById => _findById ??= new(Root);


//     public ISeekable<TResult> OrderBy<TNewSource>() where TNewSource : class
//     {
//         if (typeof(TNewSource) == typeof(TSource))
//         {
//             return this;
//         }

//         return new SourceOriginSeek<TNewSource, TResult, TRefKey, TValueKey>(
//             _query, _direction, _take, _referenceKey, _valueKey);
//     }


//     public ISeekable<TResult> UseSourceOrigin()
//     {
//         return this;
//     }


//     public ISeekable<TResult> Take(int count)
//     {
//         return new SourceOriginSeek<TSource, TResult, TRefKey, TValueKey>(
//             _query, _direction, count, _referenceKey, _valueKey);
//     }


//     public async Task<SeekList<TResult>> ToSeekListAsync(CancellationToken cancel = default)
//     {
//         var origin = await GetOriginAsync(cancel).ConfigureAwait(false);

//         return await SeekQuery(origin)
//             .ToSeekListAsync(cancel)
//             .ConfigureAwait(false);
//     }


//     private IQueryable<TResult> SeekQuery(TSource? origin)
//     {
//         var query = _query
//             .WithSelect<TSource, TResult>()
//             .SeekOrigin(origin, _direction);

//         return _take is int count
//             ? query.Take(count)
//             : query;
//     }


//     private async Task<TSource?> GetOriginAsync(CancellationToken cancel)
//     {
//         if (_referenceKey is TSource keyOrigin)
//         {
//             return keyOrigin;
//         }

//         if (_valueKey is TValueKey valueKey)
//         {
//             return await GetOriginQuery(valueKey)
//                 .SingleOrDefaultAsync(cancel)
//                 .ConfigureAwait(false);
//         }

//         if (_referenceKey is TRefKey refKey)
//         {
//             return await GetOriginQuery(refKey)
//                 .SingleOrDefaultAsync(cancel)
//                 .ConfigureAwait(false);
//         }

//         return null;
//     }


//     private IQueryable<TSource> GetOriginQuery<TKey>(TKey key)
//     {
//         var entityParameter = Expression.Parameter(
//             typeof(TSource), typeof(TSource).Name[0].ToString().ToLower());

//         var getKey = SeekHelpers.GetKeyInfo<TKey>(Root);

//         var propertyId = Expression.Property(entityParameter, getKey);

//         var orderId = Expression.Lambda<Func<TSource, TKey>>(
//             propertyId,
//             entityParameter);

//         var equalId = Expression.Lambda<Func<TSource, bool>>(
//             Expression.Equal(propertyId, Expression.Constant(key)),
//             entityParameter);

//         var originQuery = _query.Provider
//             .CreateQuery<TSource>(FindById.Visit(_query.Expression))
//             .Where(equalId)
//             .OrderBy(orderId)
//             .AsNoTracking();

//         foreach (var include in FindById.Include)
//         {
//             originQuery = originQuery.Include(include);
//         }

//         return originQuery;
//     }
// }



internal static class SeekHelpers
{
    internal static IEntityType GetEntityType<TEntity>(Expression expression)
        where TEntity : class
    {
        if (FindRootQuery.Instance.Visit(expression)
            is not QueryRootExpression { EntityType: var entity })
        {
            throw new ArgumentException("Missing a query root", nameof(expression));
        }

        if (entity.ClrType == typeof(TEntity))
        {
            return entity;
        }

        var navEntity = entity
            .GetNavigations()
            .Where(n => n.ClrType == typeof(TEntity))
            .Select(n => n.TargetEntityType)
            .FirstOrDefault();

        if (navEntity is null)
        {
            throw new InvalidOperationException($"Type {typeof(TEntity).Name} could not be found");
        }

        return navEntity;
    }


    internal static PropertyInfo GetKeyInfo<TKey>(IEntityType entity)
    {
        var entityId = entity.FindPrimaryKey();

        if (typeof(TKey) != entityId?.GetKeyType())
        {
            throw new ArgumentException($"{typeof(TKey).Name} is the not correct key type");
        }

        if (entityId is not { Properties.Count: 1, Properties: IReadOnlyList<IProperty> properties }
            || properties[0].PropertyInfo is not PropertyInfo getKey)
        {
            throw new NotSupportedException("Only single primary keys are supported");
        }

        return getKey;
    }
}