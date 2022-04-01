using System.Linq;
namespace System.Paging;

public interface ISelectableQueryable<out TSource, out TResult> : IQueryable<TResult>
{
}