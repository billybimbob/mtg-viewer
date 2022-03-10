using System.Threading;
using System.Threading.Tasks;

namespace System.Paging.Query;

public interface ISeekBuilder<TEntity>
    where TEntity : class
{
    ISeekBuilder<TEntity> WithSource<TSource>() where TSource : class;

    ISeekBuilder<TEntity> WithOriginAsSource();

    ISeekBuilder<TEntity> WithKey<TKey>(TKey? key) where TKey : class;

    ISeekBuilder<TEntity> WithKey<TKey>(TKey? key) where TKey : struct;

    ValueTask<SeekList<TEntity>> ToSeekListAsync(CancellationToken cancel = default);
}