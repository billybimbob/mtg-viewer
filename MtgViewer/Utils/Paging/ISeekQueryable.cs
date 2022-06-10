using System.Linq;

namespace EntityFrameworkCore.Paging;

public interface ISeekQueryable<out T> : IQueryable<T>
{
}
