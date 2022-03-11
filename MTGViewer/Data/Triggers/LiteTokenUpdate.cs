using System;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.Extensions.Logging;
using MTGViewer.Data.Concurrency;

namespace MTGViewer.Data.Triggers;

public class LiteTokenUpdate : IBeforeSaveTrigger<Concurrent> 
{
    private readonly ILogger<LiteTokenUpdate> _logger;

    public LiteTokenUpdate(ILogger<LiteTokenUpdate> logger)
    {
        _logger = logger;
    }


    public Task BeforeSave(ITriggerContext<Concurrent> trigContext, CancellationToken cancel)
    {
        if (trigContext.ChangeType is ChangeType.Added or ChangeType.Modified)
        {
            trigContext.Entity.LiteToken = Guid.NewGuid();
        }

        return Task.CompletedTask;
    }
}