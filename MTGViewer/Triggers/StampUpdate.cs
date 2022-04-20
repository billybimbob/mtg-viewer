using System;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Triggered;
using MTGViewer.Data;

namespace MTGViewer.Triggers;

public class StampUpdate : IBeforeSaveTrigger<Concurrent>
{
    public Task BeforeSave(ITriggerContext<Concurrent> context, CancellationToken cancellationToken)
    {
        if (context.ChangeType is ChangeType.Added or ChangeType.Modified)
        {
            context.Entity.Stamp = Guid.NewGuid();
        }

        return Task.CompletedTask;
    }
}
