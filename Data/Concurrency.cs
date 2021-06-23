using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using Microsoft.Extensions.Logging;

#if SQLiteVersion
using System;
using EntityFrameworkCore.Triggered;
#endif


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


#if SQLiteVersion
    public class GuidTokenTrigger : IBeforeSaveTrigger<Concurrent> 
    {
        private readonly ILogger<GuidTokenTrigger> _logger;

        public GuidTokenTrigger(ILogger<GuidTokenTrigger> logger)
        {
            _logger = logger;
        }

        public Task BeforeSave(ITriggerContext<Concurrent> trigContext, CancellationToken cancel)
        {
            // int id = 0;
            // if (trigContext.Entity is CardAmount amount)
            // {
            //     id = amount.Id;
            // }
            // else if (trigContext.Entity is Location location)
            // {
            //     id = location.Id;
            // }

            _logger.LogInformation($"trigger for {trigContext.Entity.GetType()}"); // with id {id}");

            if (trigContext.ChangeType == ChangeType.Modified)
            {
                // var oldTok = trigContext.Entity.ConcurrentToken;

                trigContext.Entity.ConcurrentToken = Guid.NewGuid();

                // var newTok = trigContext.Entity.ConcurrentToken;
                // _logger.LogInformation($"old: {oldTok}, vs new: {newTok}");
            }

            return Task.CompletedTask;
        }

    }
#endif

}