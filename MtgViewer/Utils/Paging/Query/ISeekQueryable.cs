using System.Linq;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

public interface ISeekQueryable<out T> : IQueryable<T> where T : class
{
    IAsyncQueryProvider AsyncProvider { get; }
}
