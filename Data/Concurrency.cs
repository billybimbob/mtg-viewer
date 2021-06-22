using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;


namespace MTGViewer.Data.Concurrency
{
    public class Concurrent
    {
#if SQLiteVersion
        [ConcurrencyCheck]
        public System.Guid ConcurrentToken { get; set; } = System.Guid.NewGuid();
#else
        [Timestamp]
        public byte[] ConcurrentToken { get; set; }
#endif
    }


    internal static class Concurrency
    {
        public static void UpdateTokens(
            this MTGCardContext context, 
            IEnumerable<Concurrent> oldEntries,
            IEnumerable<Concurrent> newEntries)
        {
            foreach(var (oldE, newE) in Enumerable.Zip(oldEntries, newEntries))
            {
                context.Entry(newE)
                    .Property(e => e.ConcurrentToken)
                    .OriginalValue = oldE.ConcurrentToken;

#if SQLiteVersion
                newE.ConcurrentToken = System.Guid.NewGuid();
#endif
            }
        }

        public static void UpdateTokens(this MTGCardContext context, IEnumerable<Concurrent> entries)
        {
            context.UpdateTokens(entries, entries);
        }


        public static void UpdateTokens(this MTGCardContext context, params Concurrent[] entries)
        {
            context.UpdateTokens((IEnumerable<Concurrent>)entries);
        }


        public static IEnumerable<EntityEntry<E>> Entries<E>(
            this DbUpdateConcurrencyException exception) where E : class =>
            exception.Entries
                .Where(en => en.Entity.GetType() == typeof(E))
                .Select(en => en.Context.Entry((E)en.Entity));

    }

}