using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;


namespace MTGViewer.Data.Triggers
{
    public class AmountValidateTrigger : IBeforeSaveTrigger<CardAmount>
    {
        private readonly CardDbContext _dbContext;
        private readonly ILogger<LiteTokenUpdateTrigger> _logger;

        public AmountValidateTrigger(CardDbContext dbContext, ILogger<LiteTokenUpdateTrigger> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task BeforeSave(ITriggerContext<CardAmount> trigContext, CancellationToken cancel)
        {
            if (trigContext.ChangeType == ChangeType.Deleted)
            {
                return;
            }

            var amount = trigContext.Entity;
            var entry = _dbContext.Entry(amount);

            if (entry.State == EntityState.Detached)
            {
                // attach just to load
                _dbContext.Attach(amount);
            }

            await entry
                .Reference(ca => ca.Location)
                .LoadAsync();

            if (amount.Location is null)
            {
                // TODO: change return location
                amount.Location = await _dbContext.Locations.FindAsync(1);
            }

            if (!amount.Location.IsShared && amount.Amount == 0)
            {
                _dbContext.Remove(amount);
            }

            else if (amount.Location.IsShared)
            {
                // makes sure that non-owned locations cannot have a request
                amount.IsRequest = false;
            }
        }

    }

}