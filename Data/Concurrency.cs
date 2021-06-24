using System;
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
        public Guid ConcurrentToken { get; set; } = Guid.NewGuid();
#else
        [Timestamp]
        public byte[] ConcurrentToken { get; set; }
#endif
    }


    internal static class Concurrency
    {
        public static void MatchToken(
            this MTGCardContext context, Concurrent current, PropertyValues dbProps)
        {
            var tokenProp = context.Entry(current).Property(c => c.ConcurrentToken);

#if SQLiteVersion
            tokenProp.OriginalValue = dbProps.GetValue<Guid>(tokenProp.Metadata);
#else
            tokenProp.OriginalValue = dbValues.GetValue<byte[]>(tokenProp.Metadata);
#endif
        }


        public static void MatchToken<E>(this MTGCardContext context, E current, E dbValues)
            where E : Concurrent
        {
            context.MatchToken(current, context.Entry(dbValues).CurrentValues);
        }


        public static IEnumerable<EntityEntry<E>> Entries<E>(this DbUpdateConcurrencyException exception)
            where E : class
        {
            return exception.Entries
                .Where(en => en.Entity is E)
                .Select(en => en.Context.Entry((E)en.Entity));
        }

    }

}