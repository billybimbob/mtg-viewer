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


        public Task BeforeSave(ITriggerContext<Exchange> trigContext, CancellationToken cancel)
        {
            if (trigContext.ChangeType == ChangeType.Deleted)
            {
                return Task.CompletedTask;
            }

            var exchange = trigContext.Entity;

            if (exchange.ToId != null && exchange.ToId == exchange.FromId
                || exchange.To != null && exchange.To == exchange.From)
            {
                throw new DbUpdateException
                    ("Trade cannot have the same location for both 'To' and 'From'");
            }

            if (exchange.ToId == null && exchange.To == null
                && exchange.FromId == null && exchange.From == null)
            {
                throw new DbUpdateException
                    ("Trade must have at least one of To or From defined");
            }

            return Task.CompletedTask;
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