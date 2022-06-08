using System.Threading;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Paging;

public interface ISeekable<TEntity> where TEntity : class
{
    ISeekable<TEntity> OrderBy<TSource>() where TSource : class;

    ISeekable<TEntity> Take(int count);

    Task<SeekList<TEntity>> ToSeekListAsync(CancellationToken cancel = default);
}
