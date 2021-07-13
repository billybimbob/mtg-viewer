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

            // TODO: improve to make more efficient, less loads
            var trade = trigContext.Entity;

            if (_dbContext.Entry(trade).State == EntityState.Detached)
            {
                _dbContext.Attach(trade);
            }

            if (!trade.IsSuggestion && trade.Amount == 0)
            {
                _dbContext.Remove(trade);
                return;
            }

            // TODO: make valid failure not silent

            var isValidFrom = await FromValidationAsync(trade);

            if (!isValidFrom)
            {
                _dbContext.Remove(trade);
                return;
            }

            var isValidTo = await ToValidationAsync(trade);

            if (!isValidTo)
            {
                _dbContext.Remove(trade);
            }
        }


        private async Task<bool> FromValidationAsync(Trade trade)
        {
            if (trade.IsSuggestion)
            {
                trade.IsCounter = false;
                return true;
            }

            await _dbContext.Entry(trade)
                .Reference(t => t.From)
                .LoadAsync();

            trade.Amount = Math.Min(trade.From.Amount, trade.Amount);
            
            await _dbContext.Entry(trade.From)
                .Reference(l => l.Location)
                .LoadAsync();

            await _dbContext.Entry(trade.From.Location)
                .Reference(l => l.Owner)
                .LoadAsync();

            trade.FromUser = trade.From.Location.Owner;

            return trade.FromUser is not null;
        }


        private async Task<bool> ToValidationAsync(Trade trade)
        {
            await _dbContext.Entry(trade)
                .Reference(t => t.To)
                .LoadAsync();

            await _dbContext.Entry(trade.To)
                .Reference(l => l.Owner)
                .LoadAsync();

            trade.ToUser = trade.To.Owner;

            return trade.ToUser is not null;
        }
    }
}