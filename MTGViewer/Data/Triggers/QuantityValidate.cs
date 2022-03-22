using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Services;

namespace MTGViewer.Data.Triggers;

public class QuantityValidate : IBeforeSaveTrigger<Quantity>
{
    private readonly PageSizes _pageSizes;
    private readonly ILogger<QuantityValidate> _logger;

    public QuantityValidate(PageSizes pageSizes, ILogger<QuantityValidate> logger)
    {
        _pageSizes = pageSizes;
        _logger = logger;
    }


    public Task BeforeSave(ITriggerContext<Quantity> trigContext, CancellationToken cancel)
    {
        if (trigContext.ChangeType is ChangeType.Deleted)
        {
            return Task.CompletedTask;
        }

        var quantity = trigContext.Entity;

        if (quantity.Copies > _pageSizes.Limit)
        {
            _logger.LogWarning(
                "Quantity {Id} has Copies {Copies} amount above limit", quantity.Id, quantity.Copies);

            quantity.Copies = _pageSizes.Limit;
        }

        if (quantity is GiveBack giveBack)
        {
            CheckGiveBack(giveBack);
            return Task.CompletedTask;
        }

        if (quantity is Want
            && quantity.Location is not TheoryCraft and not null)
        {
            throw new DbUpdateException("Want can only have a TheoryCraft type");
        }

        return Task.CompletedTask;
    }


    private void CheckGiveBack(GiveBack giveBack)
    {
        if (giveBack.Location is not Deck deck)
        {
            throw new DbUpdateException("GiveBack can only have a Deck type, and must be loaded");
        }

        var hold = deck.Holds.FirstOrDefault(h => h.CardId == giveBack.CardId);

        if (hold is null || hold.Copies == 0)
        {
            throw new DbUpdateException("GiveBack is lacking the required Hold amount");
        }

        if (hold.Copies < giveBack.Copies)
        {
            _logger.LogWarning(
                "GiveBack {Id} Copies {Copies} is too high", giveBack.Id, giveBack.Copies);

            giveBack.Copies = hold.Copies;
        }
    }
}