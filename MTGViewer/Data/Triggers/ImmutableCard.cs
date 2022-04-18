using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MTGViewer.Data.Triggers;

public class ImmutableCard : IBeforeSaveTrigger<Card>
{
    private readonly CardDbContext _dbContext;
    private readonly ILogger<ImmutableCard> _logger;

    public ImmutableCard(CardDbContext dbContext, ILogger<ImmutableCard> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task BeforeSave(ITriggerContext<Card> context, CancellationToken cancellationToken)
    {
        if (context.ChangeType is not ChangeType.Modified)
        {
            return Task.CompletedTask;
        }

        var card = context.Entity;

        _logger.LogWarning("Card {CardId} is marked for modification", card.Id);

        if (context.UnmodifiedEntity is not Card original)
        {
            throw new DbUpdateException("Card cannot be changed");
        }

        var cardEntry = _dbContext.Entry(card);

        cardEntry.CurrentValues.SetValues(original);
        cardEntry.State = EntityState.Unchanged;

        return Task.CompletedTask;
    }
}
