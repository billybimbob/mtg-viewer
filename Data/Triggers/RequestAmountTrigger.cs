using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;


namespace MTGViewer.Data.Triggers
{
    public class RequestAmountTrigger : IBeforeSaveTrigger<CardAmount>
    {
        private readonly CardDbContext _dbContext;
        private readonly ILogger<GuidTokenTrigger> _logger;

        public RequestAmountTrigger(CardDbContext dbContext, ILogger<GuidTokenTrigger> logger)
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

            if (trigContext.ChangeType == ChangeType.Modified)
            {
                var entry = _dbContext.Entry(amount);
                if (entry.State == EntityState.Detached)
                {
                    // attach just to load
                    _dbContext.Attach(amount);
                }

                await entry.Reference(ca => ca.Location).LoadAsync();
            }

            if (amount.Location == null)
            {
                // TODO: change return location
                amount.Location = await _dbContext.Locations.FindAsync(1);
            }

            // makes sure that non-owned locations cannot have a request

            if (amount.Location.IsShared)
            {
                amount.IsRequest = false;
            }
        }

    }

}