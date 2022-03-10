using System.Linq;
namespace System.Paging;

public interface ISelectableQueryable<TSource, TResult> : IQueryable<TResult>
{
}