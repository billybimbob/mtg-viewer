using System;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Triggered;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;


namespace MTGViewer.Data.Triggers
{
    public class TradeValidateTrigger : IBeforeSaveTrigger<Trade>
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
            var entry = _dbContext.Entry(trade);

            // TODO: improve to make more efficient, less loads

            if (entry.State == EntityState.Detached)
            {
                _dbContext.Attach(trade);
            }

            await entry.Reference(t => t.SrcLocation).LoadAsync();

            var srcLocPassed = await SrcLocValidationAsync(trade);

            if (!srcLocPassed)
            {
                return;
            }

            await entry.Reference(t => t.DestLocation).LoadAsync();

            await _dbContext.Entry(trade.DestLocation)
                .Reference(l => l.Owner)
                .LoadAsync();

            trade.DestUser = trade.DestLocation.Owner;
        }


        private async Task<bool> SrcLocValidationAsync(Trade trade)
        {
            if (trade.IsSuggestion)
            {
                return true;
            }

            var entry = _dbContext.Entry(trade);

            await entry.Reference(t => t.Card).LoadAsync();

            var srcAmount = await _dbContext.Amounts
                .FindAsync(trade.CardId, trade.SrcLocationId, false);

            if (srcAmount == null)
            {
                // keep eye on, not sure if want to auto remove
                _logger.LogError("src location does not have the card to trade");
                _dbContext.Remove(trade);

                return false;
            }

            trade.Amount = Math.Min(srcAmount.Amount, trade.Amount);

            await _dbContext.Entry(trade.SrcLocation)
                .Reference(l => l.Owner)
                .LoadAsync();

            trade.SrcUser = trade.SrcLocation.Owner;

            return true;
        }
    }
}