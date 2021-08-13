using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Triggered;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;


namespace MTGViewer.Data.Triggers
{
    public class TradeValidateTrigger : IBeforeSaveTrigger<Trade>, IAfterSaveTrigger<Trade>
    {
        private readonly CardDbContext _dbContext;
        private readonly ILogger<TradeValidateTrigger> _logger;

        public TradeValidateTrigger(CardDbContext dbContext, ILogger<TradeValidateTrigger> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }


        public async Task BeforeSave(ITriggerContext<Trade> trigContext, CancellationToken cancel)
        {
            if (trigContext.ChangeType == ChangeType.Deleted)
            {
                return;
            }

            var trade = trigContext.Entity;

            if (_dbContext.Entry(trade).State == EntityState.Detached)
            {
                _dbContext.Attach(trade);
            }

            if (trade.IsSuggestion)
            {
                trade.IsCounter = false;
                trade.Amount = 0;
                return;
            }

            await _dbContext.Entry(trade)
                .Reference(t => t.From)
                .LoadAsync();

            var fromAmount = await _dbContext.Amounts
                .AsNoTracking()
                .SingleOrDefaultAsync(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId);

            if (fromAmount != default)
            {
                trade.Amount = Math.Min(fromAmount.Amount, trade.Amount);
            }
        }


        public async Task AfterSave(ITriggerContext<Trade> trigContext, CancellationToken cancel)
        {
            if (trigContext.ChangeType == ChangeType.Deleted)
            {
                return;
            }

            var trade = trigContext.Entity;

            if (!trade.IsSuggestion && trade.Amount == 0)
            {
                _dbContext.Attach(trade).State = EntityState.Deleted;

                await _dbContext.SaveChangesAsync();
            }
        }
    }
}