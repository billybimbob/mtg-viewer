using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;


namespace MTGViewer.Data.Triggers
{
    public class RequestAmountTrigger : IBeforeSaveTrigger<CardAmount>
    {
        private readonly MTGCardContext _dbContext;
        private readonly ILogger<GuidTokenTrigger> _logger;

        public RequestAmountTrigger(MTGCardContext dbContext, ILogger<GuidTokenTrigger> logger)
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

            var entry = _dbContext.Entry(trigContext.Entity);

            if (entry.State != EntityState.Added)
            {
                await entry.Reference(ca => ca.Location).LoadAsync();
            }

            if (trigContext.Entity.Location == null)
            {
                var defaultLoc = await _dbContext.Locations.FindAsync(1);
                trigContext.Entity.Location = defaultLoc;
            }

            // makes sure that non-owned locations cannot have a request

            if (trigContext.Entity.Location.IsShared)
            {
                trigContext.Entity.IsRequest = false;
            }
        }

    }

}