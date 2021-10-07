using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;


namespace MTGViewer.Data.Triggers
{
    public class RequestValidate : IAfterSaveTrigger<CardRequest>
    {
        private readonly CardDbContext _dbContext;
        private readonly ILogger<RequestValidate> _logger;

        public RequestValidate(CardDbContext dbContext, ILogger<RequestValidate> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }


        public async Task AfterSave(ITriggerContext<CardRequest> trigContext, CancellationToken cancel)
        {
            if (trigContext.ChangeType == ChangeType.Deleted)
            {
                return;
            }

            var request = trigContext.Entity;

            if (request.Amount > 0)
            {
                return;
            }

            if (_dbContext.Entry(request).State == EntityState.Detached)
            {
                if (request is Want want)
                {
                    _dbContext.Wants.Attach(want);
                }
                else if (request is GiveBack give)
                {
                    _dbContext.GiveBacks.Attach(give);
                }
            }

            _dbContext.Entry(request).State = EntityState.Deleted;

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