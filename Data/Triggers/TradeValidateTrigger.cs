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

            if (_dbContext.Entry(trade).State == EntityState.Detached)
            {
                _dbContext.Attach(trade);
            }

            // TODO: make valid failure not silent

            if (trade.IsSuggestion)
            {
                trade.IsCounter = false;
                trade.Amount = 0;
                return;
            }

            await _dbContext.Entry(trade)
                .Reference(t => t.From)
                .LoadAsync();

            trade.Amount = Math.Min(trade.From.Amount, trade.Amount);

            if (trade.Amount == 0)
            {
                _dbContext.Remove(trade);
            }
        }
    }
}