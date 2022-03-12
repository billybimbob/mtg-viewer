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


    public Task BeforeSave(ITriggerContext<Card> trigContext, CancellationToken cancel)
    {
        if (trigContext is { ChangeType: not ChangeType.Modified })
        {
            return Task.CompletedTask;
        }

        if (trigContext is not { UnmodifiedEntity: Card original })
        {
            throw new DbUpdateException("Card cannot be changed");
        }

        var card = trigContext.Entity;
        var cardEntry = _dbContext.Entry(card);

        cardEntry.CurrentValues.SetValues(original);
        cardEntry.State = EntityState.Unchanged;

        return Task.CompletedTask;
    }
}