using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;


namespace MTGViewer.Data.Triggers
{
    public class AmountValidate : IAfterSaveTrigger<CardAmount>
    {
        private readonly CardDbContext _dbContext;
        private readonly ILogger<AmountValidate> _logger;

        public AmountValidate(CardDbContext dbContext, ILogger<AmountValidate> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }


        // public async Task BeforeSave(ITriggerContext<CardAmount> trigContext, CancellationToken cancel)
        // {
        //     if (trigContext.ChangeType == ChangeType.Deleted)
        //     {
        //         return;
        //     }

        //     var cardAmount = trigContext.Entity;
        //     var location = cardAmount.Location;

        //     if (_dbContext.Entry(cardAmount).State == EntityState.Detached)
        //     {
        //         // attach just to load
        //         _dbContext.Attach(cardAmount);
        //     }

        //     await _dbContext.Entry(cardAmount)
        //         .Reference(ca => ca.Location)
        //         .LoadAsync();

        //     if (cardAmount.Location is Box)
        //     {
        //         // makes sure that non-owned locations cannot have a request
        //         cardAmount.IsRequest = false;
        //     }
        // }


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
                _dbContext.Amounts.Attach(cardAmount);
            }

            if (cardAmount.Amount > 0)
            {
                return;
            }

            await _dbContext.Entry(cardAmount)
                .Reference(ca => ca.Location)
                .LoadAsync();

            if (cardAmount.Location is not Deck)
            {
                return;
            }

            _dbContext.Entry(cardAmount).State = EntityState.Deleted;

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