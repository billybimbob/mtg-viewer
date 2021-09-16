using System;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Triggered;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;


namespace MTGViewer.Data.Triggers
{
    public class TradeValidate : IBeforeSaveTrigger<Trade>, IAfterSaveTrigger<Trade>
    {
        private readonly CardDbContext _dbContext;
        private readonly ILogger<TradeValidate> _logger;

        public TradeValidate(CardDbContext dbContext, ILogger<TradeValidate> logger)
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

            // if (transfer.ToId is null && transfer.To is null)
            // {
            //     throw new DbUpdateException("Trade cannot have 'To' property missing");
            // }

            var fromAmount = await _dbContext.DeckAmounts
                .AsNoTracking()
                .SingleOrDefaultAsync(ca =>
                    !ca.HasIntent
                        && ca.CardId == trade.CardId
                        && (ca.LocationId == trade.FromId || ca.Location == trade.From));

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

            if (_dbContext.Entry(trade).State == EntityState.Detached)
            {
                _dbContext.Attach(trade);
            }

            if (trade.Amount > 0)
            {
                return;
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