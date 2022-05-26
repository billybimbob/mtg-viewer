using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace MtgViewer.Utils;

public static class ConcurrencyExtensions
{
    public static IEnumerable<EntityEntry<TEntity>> Entries<TEntity>(this DbUpdateConcurrencyException exception)
        where TEntity : class
    {
        return exception.Entries
            .Where(en => en.Entity is TEntity)
            .Select(en => en.Context.Entry((TEntity)en.Entity));
    }
}
