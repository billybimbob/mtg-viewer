using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MtgViewer.Data;
using MtgViewer.Services;

namespace MtgViewer.Triggers;

public class QuantityValidate : IBeforeSaveTrigger<Quantity>
{
    private readonly PageSize _pageSize;
    private readonly ILogger<QuantityValidate> _logger;

    public QuantityValidate(PageSize pageSize, ILogger<QuantityValidate> logger)
    {
        _pageSize = pageSize;
        _logger = logger;
    }

    public Task BeforeSave(ITriggerContext<Quantity> context, CancellationToken cancellationToken)
    {
        if (context.ChangeType is ChangeType.Deleted)
        {
            return Task.CompletedTask;
        }

        var quantity = context.Entity;

        if (quantity.Copies > _pageSize.Limit)
        {
            _logger.LogWarning(
                "Quantity {Id} has Copies {Copies} amount above limit", quantity.Id, quantity.Copies);

            quantity.Copies = _pageSize.Limit;
        }

        if (quantity is not Giveback giveback)
        {
            return Task.CompletedTask;
        }

        if (giveback.Location is not Deck deck)
        {
            throw new DbUpdateException("Giveback can only have a Deck type, and must be loaded");
        }

        var hold = deck.Holds.FirstOrDefault(h => h.CardId == giveback.CardId);

        if (hold is null || hold.Copies == 0)
        {
            throw new DbUpdateException("Giveback is lacking the required Hold amount");
        }

        if (hold.Copies < giveback.Copies)
        {
            _logger.LogWarning(
                "Giveback {Id} Copies {Copies} is too high", giveback.Id, giveback.Copies);

            giveback.Copies = hold.Copies;
        }

        return Task.CompletedTask;
    }
}
