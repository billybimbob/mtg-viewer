using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;


namespace MTGViewer.Data.Triggers
{
    public class AmountValidateTrigger : IBeforeSaveTrigger<CardAmount>, IAfterSaveTrigger<CardAmount>
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

            var cardAmount = trigContext.Entity;

            if (_dbContext.Entry(cardAmount).State == EntityState.Detached)
            {
                // attach just to load
                _dbContext.Attach(cardAmount);
            }

            await _dbContext.Entry(cardAmount)
                .Reference(ca => ca.Location)
                .LoadAsync();

            if (cardAmount.Location is null)
            {
                // TODO: change return location
                cardAmount.Location = await _dbContext.Locations.FindAsync(1);
            }

            if (cardAmount.Location.IsShared)
            {
                // makes sure that non-owned locations cannot have a request
                cardAmount.IsRequest = false;
            }
        }


        public async Task AfterSave(ITriggerContext<CardAmount> trigContext, CancellationToken cancel)
        {
            if (trigContext.ChangeType == ChangeType.Deleted)
            {
                return;
            }

            var cardAmount = trigContext.Entity;

            if (cardAmount.Amount == 0)
            {
                _dbContext.Attach(cardAmount).State = EntityState.Deleted;

                await _dbContext.SaveChangesAsync();
            }
        }

    }

}