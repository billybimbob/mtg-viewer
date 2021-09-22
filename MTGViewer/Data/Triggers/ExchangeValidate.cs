using System;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Triggered;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;


namespace MTGViewer.Data.Triggers
{
    public class ExchangeValidate : IBeforeSaveTrigger<Exchange>, IAfterSaveTrigger<Exchange>
    {
        private readonly CardDbContext _dbContext;
        private readonly ILogger<ExchangeValidate> _logger;

        public ExchangeValidate(CardDbContext dbContext, ILogger<ExchangeValidate> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }


        public async Task BeforeSave(ITriggerContext<Exchange> trigContext, CancellationToken cancel)
        {
            if (trigContext.ChangeType == ChangeType.Deleted)
            {
                return;
            }

            var exchange = trigContext.Entity;

            if (exchange.ToId == exchange.FromId && exchange.To == exchange.From)
            {
                throw new DbUpdateException
                    ("Trade cannot have the same location for both 'To' and 'From'");
            }

            int capTargetId;
            Deck capTarget;

            if (exchange.FromId == default && exchange.From == default)
            {
                capTargetId = exchange.ToId.Value;
                capTarget = exchange.To;
            }
            else
            {
                capTargetId = exchange.FromId.Value;
                capTarget = exchange.To;
            }

            var capAmount = await _dbContext.Amounts
                .AsNoTracking()
                .SingleOrDefaultAsync(ca => ca.CardId == exchange.CardId
                    && (ca.LocationId == capTargetId || ca.Location == capTarget));

            if (capAmount != default)
            {
                exchange.Amount = Math.Min(capAmount.Amount, exchange.Amount);
            }
        }


        public async Task AfterSave(ITriggerContext<Exchange> trigContext, CancellationToken cancel)
        {
            if (trigContext.ChangeType == ChangeType.Deleted)
            {
                return;
            }

            var trade = trigContext.Entity;

            if (trade.Amount > 0)
            {
                return;
            }

            if (_dbContext.Entry(trade).State == EntityState.Detached)
            {
                _dbContext.Exchanges.Attach(trade);
            }

            _dbContext.Entry(trade).State = EntityState.Deleted;

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                _logger.LogError(e.ToString());
            }
        }
    }
}