using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Services;

namespace MTGViewer.Data.Triggers;

public class QuantityValidate : IBeforeSaveTrigger<Quantity>, IAfterSaveTrigger<Quantity>
{
    private readonly CardDbContext _dbContext;
    private readonly PageSizes _pageSizes;
    private readonly ILogger<QuantityValidate> _logger;

    public QuantityValidate(
        CardDbContext dbContext,
        PageSizes pageSizes,
        ILogger<QuantityValidate> logger)
    {
        _dbContext = dbContext;
        _pageSizes = pageSizes;
        _logger = logger;
    }


    public Task BeforeSave(ITriggerContext<Quantity> trigContext, CancellationToken cancel)
    {
        if (trigContext.ChangeType == ChangeType.Deleted)
        {
            return Task.CompletedTask;
        }

        var quantity = trigContext.Entity;

        if (quantity.NumCopies > _pageSizes.Limit)
        {
            quantity.NumCopies = _pageSizes.Limit;
        }

        return Task.CompletedTask;
    }


    public async Task AfterSave(ITriggerContext<Quantity> trigContext, CancellationToken cancel)
    {
        if (trigContext.ChangeType == ChangeType.Deleted)
        {
            return;
        }

        var quantity = trigContext.Entity;

        if (quantity.NumCopies > 0)
        {
            return;
        }

        if (_dbContext.Entry(quantity).State == EntityState.Detached)
        {
            if (quantity is Amount amount)
            {
                _dbContext.Amounts.Attach(amount);
            }
            else if (quantity is Want want)
            {
                _dbContext.Wants.Attach(want);
            }
            else if (quantity is GiveBack give)
            {
                _dbContext.GiveBacks.Attach(give);
            }
        }

        await _dbContext.Entry(quantity)
            .Reference(q => q.Location)
            .LoadAsync();

        _dbContext.Entry(quantity).State = EntityState.Deleted;

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