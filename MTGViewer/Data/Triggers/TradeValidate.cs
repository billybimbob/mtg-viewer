using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;

namespace MTGViewer.Data.Triggers;

public class TradeValidate : IBeforeSaveTrigger<Trade>
{
    public Task BeforeSave(ITriggerContext<Trade> context, CancellationToken cancellationToken)
    {
        if (context.ChangeType is ChangeType.Deleted)
        {
            return Task.CompletedTask;
        }

        var trade = context.Entity;

        if (trade.ToId == trade.FromId && trade.To == trade.From)
        {
            throw new DbUpdateException(
                "Trade cannot have the same location for both 'To' and 'From'");
        }

        return Task.CompletedTask;
    }
}
