using System.Linq;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

public interface ISeekable<out T> : IQueryable<T>
{
    IAsyncQueryProvider AsyncProvider { get; }
}
