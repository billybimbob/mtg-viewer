using System.Linq;

namespace EntityFrameworkCore.Paging;

public interface ISelectableQueryable<out TSource, out TResult> : IQueryable<TResult>
{
}
