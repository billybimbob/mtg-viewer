using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

internal sealed class ResultNullSeek<TEntity> : ISeekBuilder<TEntity>
    where TEntity : class
{
    private readonly IQueryable<TEntity> _query;
    private readonly int _pageSize;
    private readonly bool _backtrack;

    internal ResultNullSeek(
        IQueryable<TEntity> query,
        int pageSize,
        bool backtrack)
    {
        ArgumentNullException.ThrowIfNull(query);

        _query = query;
        _pageSize = pageSize;
        _backtrack = backtrack;
    }


    public ISeekBuilder<TEntity> WithSource<TSource>() where TSource : class
    {
        // use ValueTuple as a placeholder type

        if (typeof(TSource) == typeof(TEntity))
        {
            return this;
        }

        return new ResultOriginSeek<TSource, TEntity, object, ValueTuple>(
            _query, _pageSize, _backtrack, null, null);
    }


    public ISeekBuilder<TEntity> WithOriginAsSource()
    {
        // use ValueTuple as a placeholder type

        return new SourceOriginSeek<TEntity, TEntity, object, ValueTuple>(
            _query, _pageSize, _backtrack, null, null);
    }


    public ISeekBuilder<TEntity> WithKey<TKey>(TKey? key) where TKey : class
    {
        // use ValueTuple as a placeholder type

        return new EntityOriginSeek<TEntity, TKey, ValueTuple>(
            _query, _pageSize, _backtrack, key, null);
    }


    public ISeekBuilder<TEntity> WithKey<TKey>(TKey? key) where TKey : struct
    {
        return new EntityOriginSeek<TEntity, object, TKey>(
            _query, _pageSize, _backtrack, null, key);
    }


    public async ValueTask<SeekList<TEntity>> ToSeekListAsync(CancellationToken cancel = default)
    {
        return await _query
            .SeekOrigin(null as TEntity, _pageSize, _backtrack)
            .ToSeekListAsync(cancel)
            .ConfigureAwait(false);
    }
}



internal sealed class SourceOriginSeek<TSource, TResult, TRefKey, TValueKey> : ISeekBuilder<TResult>
    where TSource : class
    where TResult : class
    where TRefKey : class
    where TValueKey : struct
{   
    private readonly IQueryable<TResult> _query;
    private readonly int _pageSize;
    private readonly bool _backtrack;

    private readonly TRefKey? _referenceKey;
    private readonly TValueKey? _valueKey;

    internal SourceOriginSeek(
        IQueryable<TResult> query,
        int pageSize,
        bool backtrack,
        TRefKey? refKey,
        TValueKey? valueKey)
    {
        ArgumentNullException.ThrowIfNull(query);

        _query = query;
        _pageSize = pageSize;
        _backtrack = backtrack;

        _referenceKey = refKey;
        _valueKey = valueKey;
    }


    private QueryRootExpression? _root;
    private QueryRootExpression Root =>
        _root ??= SeekHelpers.GetRoot<TSource>(_query.Expression);


    private FindByIdVisitor? _findById;
    private FindByIdVisitor FindById => _findById ??= new(Root);


    public ISeekBuilder<TResult> WithSource<TNewSource>() where TNewSource : class
    {
        if (typeof(TNewSource) == typeof(TSource))
        {
            return this;
        }

        return new SourceOriginSeek<TNewSource, TResult, TRefKey, TValueKey>(
            _query, _pageSize, _backtrack, _referenceKey, _valueKey);
    }


    public ISeekBuilder<TResult> WithOriginAsSource()
    {
        return this;
    }


    public ISeekBuilder<TResult> WithKey<TKey>(TKey? key) where TKey : class
    {
        // use ValueTuple as a placeholder type

        return new SourceOriginSeek<TSource, TResult, TKey, TValueKey>(
            _query, _pageSize, _backtrack, key, null);
    }


    public ISeekBuilder<TResult> WithKey<TKey>(TKey? key) where TKey : struct
    {
        return new SourceOriginSeek<TSource, TResult, TRefKey, TKey>(
            _query, _pageSize, _backtrack, null, key);
    }


    public async ValueTask<SeekList<TResult>> ToSeekListAsync(CancellationToken cancel = default)
    {
        var origin = await GetOriginAsync(cancel).ConfigureAwait(false);

        return await _query
            .WithSelect<TSource, TResult>()
            .SeekOrigin(origin, _pageSize, _backtrack)
            .ToSeekListAsync(cancel)
            .ConfigureAwait(false);
    }
    

    private async ValueTask<TSource?> GetOriginAsync(CancellationToken cancel)
    {
        if (_referenceKey is TSource keyOrigin)
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


    private IQueryable<TSource> GetOriginQuery<TKey>(TKey key)
    {
        var entityParameter = Expression.Parameter(
            typeof(TSource), typeof(TSource).Name[0].ToString().ToLower());

        var getKey = SeekHelpers.GetKeyInfo<TKey>(Root);

        var propertyId = Expression.Property(entityParameter, getKey);

        var orderId = Expression.Lambda<Func<TSource, TKey>>(
            propertyId,
            entityParameter);

        var equalId = Expression.Lambda<Func<TSource, bool>>(
            Expression.Equal(propertyId, Expression.Constant(key)),
            entityParameter);

        var originQuery = _query.Provider
            .CreateQuery<TSource>(FindById.Visit(_query.Expression))
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



internal sealed class ResultOriginSeek<TSource, TResult, TRefKey, TValueKey> : ISeekBuilder<TResult>
    where TSource : class
    where TResult : class
    where TRefKey : class
    where TValueKey : struct
{
    private readonly IQueryable<TResult> _query;
    private readonly int _pageSize;
    private readonly bool _backtrack;

    private readonly TRefKey? _referenceKey;
    private readonly TValueKey? _valueKey;

    internal ResultOriginSeek(
        IQueryable<TResult> query,
        int pageSize,
        bool backtrack,
        TRefKey? refKey,
        TValueKey? valueKey)
    {
        ArgumentNullException.ThrowIfNull(query);

        _query = query;
        _pageSize = pageSize;
        _backtrack = backtrack;

        _referenceKey = refKey;
        _valueKey = valueKey;
    }

    private QueryRootExpression? _root;
    private QueryRootExpression Root =>
        _root ??= SeekHelpers.GetRoot<TSource>(_query.Expression);


    private FindByIdVisitor? _findById;
    private FindByIdVisitor FindById => _findById ??= new(null);


    private SelectResult<TSource, TResult>? _result;
    private SelectResult<TSource, TResult> SelectResult =>
        _result ??= SelectQueries.GetSelectQuery<TSource, TResult>(_query);



    public ISeekBuilder<TResult> WithSource<TNewSource>()
        where TNewSource : class
    {
        if (typeof(TNewSource) == typeof(TSource))
        {
            return this;
        }

        return new ResultOriginSeek<TNewSource, TResult, TRefKey, TValueKey>(
            _query, _pageSize, _backtrack, _referenceKey, _valueKey);
    }


    public ISeekBuilder<TResult> WithOriginAsSource()
    {
        return new SourceOriginSeek<TSource, TResult, TRefKey, TValueKey>(
            _query, _pageSize, _backtrack, _referenceKey, _valueKey);
    }


    public ISeekBuilder<TResult> WithKey<TKey>(TKey? key) where TKey : class
    {
        return new ResultOriginSeek<TSource, TResult, TKey, TValueKey>(
            _query, _pageSize, _backtrack, key, null);
    }


    public ISeekBuilder<TResult> WithKey<TKey>(TKey? key) where TKey : struct
    {
        return new ResultOriginSeek<TSource, TResult, TRefKey, TKey>(
            _query, _pageSize, _backtrack, null, key);
    }


    public async ValueTask<SeekList<TResult>> ToSeekListAsync(CancellationToken cancel = default)
    {
        bool hasSelector = FindSelect<TSource, TResult>.Instance
            .Visit(_query.Expression) is Expression<Func<TSource, TResult>>;

        var origin = await GetOriginAsync(cancel).ConfigureAwait(false);

        var query = hasSelector
            ? _query
                .WithSelect<TSource, TResult>()
                .SeekOrigin(origin, _pageSize, _backtrack)
            : _query
                .SeekOrigin(origin, _pageSize, _backtrack);

        return await query
            .ToSeekListAsync(cancel)
            .ConfigureAwait(false);
    }


    private async ValueTask<TResult?> GetOriginAsync(CancellationToken cancel)
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

        var getKey = SeekHelpers.GetKeyInfo<TKey>(Root);

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



internal sealed class EntityOriginSeek<TEntity, TRefKey, TValueKey> : ISeekBuilder<TEntity>
    where TEntity : class
    where TRefKey : class
    where TValueKey : struct
{
    private readonly IQueryable<TEntity> _query;
    private readonly int _pageSize;
    private readonly bool _backtrack;

    private readonly TRefKey? _referenceKey;
    private readonly TValueKey? _valueKey;

    internal EntityOriginSeek(
        IQueryable<TEntity> query,
        int pageSize,
        bool backtrack,
        TRefKey? refKey,
        TValueKey? valueKey)
    {
        ArgumentNullException.ThrowIfNull(query);

        _query = query;
        _pageSize = pageSize;
        _backtrack = backtrack;

        _referenceKey = refKey;
        _valueKey = valueKey;
    }

    private QueryRootExpression? _root;
    private QueryRootExpression Root =>
        _root ??= SeekHelpers.GetRoot<TEntity>(_query.Expression);


    private FindByIdVisitor? _findById;
    private FindByIdVisitor FindById => _findById ??= new(Root);


    public ISeekBuilder<TEntity> WithSource<TSource>()
        where TSource : class
    {
        if (typeof(TSource) == typeof(TEntity))
        {
            return this;
        }

        return new ResultOriginSeek<TSource, TEntity, TRefKey, TValueKey>(
            _query, _pageSize, _backtrack, _referenceKey, _valueKey);
    }


    public ISeekBuilder<TEntity> WithOriginAsSource()
    {
        return new SourceOriginSeek<TEntity, TEntity, TRefKey, TValueKey>(
            _query, _pageSize, _backtrack, _referenceKey, _valueKey);
    }


    public ISeekBuilder<TEntity> WithKey<TKey>(TKey? key) where TKey : class
    {
        return new EntityOriginSeek<TEntity, TKey, TValueKey>(
            _query, _pageSize, _backtrack, key, null);
    }


    public ISeekBuilder<TEntity> WithKey<TKey>(TKey? key) where TKey : struct
    {
        return new EntityOriginSeek<TEntity, TRefKey, TKey>(
            _query, _pageSize, _backtrack, null, key);
    }


    public async ValueTask<SeekList<TEntity>> ToSeekListAsync(CancellationToken cancel = default)
    {
        var origin = await GetOriginAsync(cancel).ConfigureAwait(false);

        return await _query
            .SeekOrigin(origin, _pageSize, _backtrack)
            .ToSeekListAsync(cancel)
            .ConfigureAwait(false);
    }


    private async ValueTask<TEntity?> GetOriginAsync(CancellationToken cancel)
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

        var getKey = SeekHelpers.GetKeyInfo<TKey>(Root);

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



internal static class SeekHelpers
{
    internal static QueryRootExpression GetRoot<TEntity>(Expression expression)
    {
        if (FindRootQuery.Instance.Visit(expression)
            is not QueryRootExpression root
            || root.EntityType.ClrType != typeof(TEntity))
        {
            throw new InvalidOperationException("Entity is not the correct type");
        }

        return root;
    }


    internal static PropertyInfo GetKeyInfo<TKey>(QueryRootExpression root)
    {
        var entityId = root.EntityType.FindPrimaryKey();

        if (typeof(TKey) != entityId?.GetKeyType())
        {
            throw new ArgumentException($"key is the not correct key type");
        }

        if (entityId?.Properties.FirstOrDefault()?.PropertyInfo is not PropertyInfo getKey)
        {
            throw new NotSupportedException("Only single primary keys are supported");
        }

        return getKey;
    }
}