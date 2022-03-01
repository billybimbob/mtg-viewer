using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace System.Paging;

public class SeekBuilder<TEntity, TKey>
    where TEntity : class
    where TKey : IEquatable<TKey>
{
    private readonly IQueryable<TEntity> _query;
    private readonly Expression<Func<TEntity, TKey>> _sourceKey;

    private readonly int _pageSize;
    private readonly bool _backtrack;

    private readonly TKey? _key; // private since reference type

    public SeekBuilder(
        IQueryable<TEntity> query, 
        Expression<Func<TEntity, TKey>> keyProperty,
        TKey? key,
        int pageSize,
        bool backtrack)
    {
        ArgumentNullException.ThrowIfNull(query);

        _query = query;
        _sourceKey = keyProperty;

        _pageSize = pageSize;
        _backtrack = backtrack;

        _key = key;
    }


    public async Task<SeekList<TEntity>> ToSeekListAsync(CancellationToken cancel = default)
    {
        var origin = await GetOriginAsync(cancel).ConfigureAwait(false);

        return await _query
            .SeekBy(origin, _pageSize, _backtrack)
            .ToSeekListAsync(_sourceKey.Compile(), cancel)
            .ConfigureAwait(false);
    }


    protected virtual async Task<TEntity?> GetOriginAsync(CancellationToken cancel)
    {
        return _key switch
        {
            TEntity keyOrigin => keyOrigin,
            TKey notNull => await FindNonNullOriginAsync(notNull, cancel)
                .ConfigureAwait(false),

            _ => null,
        };
    }


    protected Task<TEntity?> FindNonNullOriginAsync(
        TKey key,
        CancellationToken cancellationToken)
    {
        var entityParameter = Expression.Parameter(
            typeof(TEntity), typeof(TEntity).Name[0].ToString().ToLower());
        
        if (_sourceKey.Body is not MemberExpression member
            || member.Member is not PropertyInfo property)
        {
            throw new InvalidOperationException("No key property is defined");
        }

        var paramId = Expression.Property(entityParameter, property);

        var idLambda = Expression.Lambda<Func<TEntity, TKey>>(
            paramId,
            entityParameter);

        var equalSeek = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(paramId, Expression.Constant(key)),
            entityParameter);

        return _query
            .OrderBy(idLambda) // intentionally override order
            .AsNoTracking()
            .SingleOrDefaultAsync(equalSeek, cancellationToken);
    }
}


internal class NullableSeekBuilder<TEntity, TKey> : SeekBuilder<TEntity, TKey>
    where TEntity : class
    where TKey : struct, IEquatable<TKey>
{
    private readonly TKey? _key;

    public NullableSeekBuilder(
        IQueryable<TEntity> query,
        Expression<Func<TEntity, TKey>> keyProperty,
        TKey? key,
        int pageSize,
        bool backtrack) : base(query, keyProperty, default, pageSize, backtrack)
    {
        _key = key;
    }

    protected override async Task<TEntity?> GetOriginAsync(CancellationToken cancel)
    {
        return _key is TKey key
            ? await base.FindNonNullOriginAsync(key, cancel).ConfigureAwait(false)
            : null;
    }
}
