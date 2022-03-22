using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Triggered;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MTGViewer.Data.Triggers;

public class TradeValidate : IBeforeSaveTrigger<Trade>
{
    private readonly ILogger<TradeValidate> _logger;

    public TradeValidate(ILogger<TradeValidate> logger)
    {
        _logger = logger;
    }

    public Task BeforeSave(ITriggerContext<Trade> trigContext, CancellationToken cancel)
    {
        if (trigContext.ChangeType is ChangeType.Deleted)
        {
            return Task.CompletedTask;
        }

        var trade = trigContext.Entity;

        if (trade.ToId == trade.FromId)
        {
            throw new DbUpdateException(
                "Trade cannot have the same location for both 'To' and 'From'");
        }

        return Task.CompletedTask;
    }
}