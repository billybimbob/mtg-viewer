using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;


namespace MTGViewer.Data.Triggers
{
    public class AmountValidate : IBeforeSaveTrigger<CardAmount>, IAfterSaveTrigger<CardAmount>
    {
        private readonly CardDbContext _dbContext;
        private readonly ILogger<LiteTokenUpdate> _logger;

        public AmountValidate(CardDbContext dbContext, ILogger<LiteTokenUpdate> logger)
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
            var location = cardAmount.Location;

            if (location is null)
            {
                if (cardAmount.LocationId == default)
                {
                    // TODO: change return location
                    cardAmount.Location = await _dbContext.Shares.FindAsync(1);
                    location = cardAmount.Location;
                }
                else
                {
                    location = await _dbContext.Locations
                        .AsNoTracking()
                        .SingleAsync(l => l.Id == cardAmount.LocationId);
                }
            }

            if (location.Type == Discriminator.Shared)
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

            if (_dbContext.Entry(cardAmount).State == EntityState.Detached)
            {
                // attach just to load
                _dbContext.Attach(cardAmount);
            }

            Location location;

            if (cardAmount.Location is null)
            {
                location = await _dbContext.Locations
                    .AsNoTracking()
                    .SingleAsync(l => l.Id == cardAmount.LocationId);
            }
            else
            {
                location = cardAmount.Location;
            }

            if (location.Type == Discriminator.Deck && cardAmount.Amount == 0)
            {
                _dbContext.Entry(cardAmount).State = EntityState.Deleted;

                await _dbContext.SaveChangesAsync();
            }
        }

    }

}