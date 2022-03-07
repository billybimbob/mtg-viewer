using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;


public abstract class SeekBuilder<TEntity, TOrigin>
    where TEntity : class
    where TOrigin : class
{
    public IQueryable<TEntity> Query { get; }
    public int PageSize { get; }
    public bool Backtrack { get; }

    protected SeekBuilder(IQueryable<TEntity> query, int pageSize, bool backtrack)
    {
        ArgumentNullException.ThrowIfNull(query);

        Query = query;
        PageSize = pageSize;
        Backtrack = backtrack;
    }


    private QueryRootExpression? _root;
    private QueryRootExpression Root
    {
        get
        {
            if (_root is not null)
            {
                return _root;
            }

            if (FindRootQuery.Instance.Visit(Query.Expression)
                is not QueryRootExpression root
                || root.EntityType.ClrType != typeof(TOrigin))
            {
                throw new InvalidOperationException("Origin is not the correct type");
            }

            return _root = root;
        }
    }

    private FindByIdVisitor? _findById;
    internal FindByIdVisitor FindById => _findById ??= new(Root);



    public abstract SeekBuilder<TEntity, TNewOrigin> WithOrigin<TNewOrigin>()
        where TNewOrigin : class;


    public SeekBuilder<TEntity, TOrigin> WithKey<TKey>(TKey? key) where TKey : class
    {
        return new SeekBuilder<TEntity, TOrigin, TKey>(Query, PageSize, Backtrack, key);
    }


    public SeekBuilder<TEntity, TOrigin> WithKey<TKey>(TKey? key) where TKey : struct
    {
        return new NullableSeekBuilder<TEntity, TOrigin, TKey>(Query, PageSize, Backtrack, key);
    }



    public async Task<SeekList<TEntity>> ToSeekListAsync(CancellationToken cancel = default)
    {
        var origin = await GetOriginAsync(cancel).ConfigureAwait(false);

        return await Query
            .SeekOrigin(origin, PageSize, Backtrack)
            .ToSeekListAsync(cancel)
            .ConfigureAwait(false);
    }


    protected abstract ValueTask<TOrigin?> GetOriginAsync(CancellationToken cancel);


    protected IQueryable<TOrigin> GetOriginQuery<TKey>(TKey key)
    {
        var getKey = GetKeyInfo<TKey>();

        var entityParameter = Expression.Parameter(
            typeof(TOrigin), typeof(TOrigin).Name[0].ToString().ToLower());

        var propertyId = Expression.Property(entityParameter, getKey);

        var orderId = Expression.Lambda<Func<TOrigin, TKey>>(
            propertyId,
            entityParameter);

        var equalId = Expression.Lambda<Func<TOrigin, bool>>(
            Expression.Equal(propertyId, Expression.Constant(key)),
            entityParameter);

        var originQuery = Query.Provider
            .CreateQuery<TOrigin>(FindById.Visit(Query.Expression));

        foreach (var include in FindById.Include)
        {
            originQuery = originQuery.Include(include);
        }

        return originQuery
            .Where(equalId)
            .OrderBy(orderId)
            .AsNoTracking();
    }


    private PropertyInfo GetKeyInfo<TKey>()
    {
        var entityId = Root.EntityType.FindPrimaryKey();

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


internal class SeekBuilder<TEntity, TOrigin, TKey> : SeekBuilder<TEntity, TOrigin>
    where TEntity : class
    where TOrigin : class
    where TKey : class
{
    private readonly TKey? _key;

    internal SeekBuilder(IQueryable<TEntity> query, int pageSize, bool backtrack, TKey? key)
        : base(query, pageSize, backtrack)
    {
        _key = key;
    }

    public override SeekBuilder<TEntity, TNewOrigin> WithOrigin<TNewOrigin>()
        where TNewOrigin : class
    {
        return new SeekBuilder<TEntity, TNewOrigin, TKey>(Query, PageSize, Backtrack, _key);
    }

    protected override async ValueTask<TOrigin?> GetOriginAsync(CancellationToken cancel)
    {
        if (_key is TOrigin keyOrigin)
        {
            return keyOrigin;
        }

        if (_key is not TKey key)
        {
            return null;
        }

        return await GetOriginQuery(key)
            .SingleOrDefaultAsync(cancel)
            .ConfigureAwait(false);
    }
}


internal class NullableSeekBuilder<TEntity, TOrigin, TKey> : SeekBuilder<TEntity, TOrigin>
    where TEntity : class
    where TOrigin : class
    where TKey : struct
{
    private readonly TKey? _key;

    internal NullableSeekBuilder(IQueryable<TEntity> query, int pageSize, bool backtrack, TKey? key)
        : base(query, pageSize, backtrack)
    {
        _key = key;
    }

    public override SeekBuilder<TEntity, TNewOrigin> WithOrigin<TNewOrigin>()
        where TNewOrigin : class
    {
        return new NullableSeekBuilder<TEntity, TNewOrigin, TKey>(Query, PageSize, Backtrack, _key);
    }

    protected override async ValueTask<TOrigin?> GetOriginAsync(CancellationToken cancel)
    {
        if (_key is not TKey key)
        {
            return null;
        }

        return await GetOriginQuery(key)
            .SingleOrDefaultAsync(cancel)
            .ConfigureAwait(false);
    }
}
