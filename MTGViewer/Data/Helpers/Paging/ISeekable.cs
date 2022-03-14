using System.Threading;
using System.Threading.Tasks;

namespace System.Paging.Query;

public interface ISeekable<TEntity> where TEntity : class
{
    ISeekable<TEntity> OrderBy<TSource>() where TSource : class;

    // ISeekable<TEntity> UseSourceOrigin();

    ISeekable<TEntity> Take(int count);

    ValueTask<SeekList<TEntity>> ToSeekListAsync(CancellationToken cancel = default);
}