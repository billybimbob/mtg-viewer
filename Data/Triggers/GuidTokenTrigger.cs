using System;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Triggered;
using Microsoft.Extensions.Logging;
using MTGViewer.Data.Concurrency;


namespace MTGViewer.Data.Triggers
{
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

                trigContext.Entity.LiteToken = Guid.NewGuid();

                // var newTok = trigContext.Entity.ConcurrentToken;
                // _logger.LogInformation($"old: {oldTok}, vs new: {newTok}");
            }

            return Task.CompletedTask;
        }
    }
}