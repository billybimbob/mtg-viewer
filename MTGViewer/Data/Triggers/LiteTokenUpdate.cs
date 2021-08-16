using System;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Triggered;
using Microsoft.Extensions.Logging;
using MTGViewer.Data.Concurrency;


namespace MTGViewer.Data.Triggers
{
    public class LiteTokenUpdate : IBeforeSaveTrigger<Concurrent> 
    {
        private readonly ILogger<LiteTokenUpdate> _logger;

        public LiteTokenUpdate(ILogger<LiteTokenUpdate> logger)
        {
            _logger = logger;
        }

        public Task BeforeSave(ITriggerContext<Concurrent> trigContext, CancellationToken cancel)
        {
            _logger.LogInformation($"trigger for {trigContext.Entity.GetType()}");

            if (trigContext.ChangeType == ChangeType.Modified)
            {
                var oldTok = trigContext.Entity.LiteToken;

                trigContext.Entity.LiteToken = Guid.NewGuid();

                var newTok = trigContext.Entity.LiteToken;
                _logger.LogInformation($"old: {oldTok}, vs new: {newTok}");
            }

            return Task.CompletedTask;
        }
    }
}