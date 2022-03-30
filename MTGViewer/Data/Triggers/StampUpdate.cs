using System;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Triggered;
using MTGViewer.Data.Concurrency;

namespace MTGViewer.Data.Triggers;

public class StampUpdate : IBeforeSaveTrigger<Concurrent>
{
    public Task BeforeSave(ITriggerContext<Concurrent> trigContext, CancellationToken cancel)
    {
        if (trigContext.ChangeType is ChangeType.Added or ChangeType.Modified)
        {
            trigContext.Entity.Stamp = Guid.NewGuid();
        }

        return Task.CompletedTask;
    }
}