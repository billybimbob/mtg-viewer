using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;

namespace MTGViewer.Services.Infrastructure;

public class ResetHandler
{
    private readonly LoadingProgress _loadProgress;

    // treat each write function as a unit of work
    // read functions can use the same db context
    private readonly IDbContextFactory<CardDbContext> _dbFactory;

    public ResetHandler(
        LoadingProgress loadProgress,
        IDbContextFactory<CardDbContext> dbFactory)
    {
        _loadProgress = loadProgress;
        _dbFactory = dbFactory;
    }

    public async Task ResetAsync(CancellationToken cancel = default)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        var data = CardStream.Reset(dbContext);

        _loadProgress.Ticks = 7;

        await foreach (var card in data.Cards.WithCancellation(cancel))
        {
            dbContext.Cards.Remove(card);
        }

        _loadProgress.AddProgress();

        await foreach (var deck in data.Decks.WithCancellation(cancel))
        {
            dbContext.Holds.RemoveRange(deck.Holds);
            dbContext.Wants.RemoveRange(deck.Wants);
            dbContext.Givebacks.RemoveRange(deck.Givebacks);

            dbContext.Decks.Remove(deck);
        }

        _loadProgress.AddProgress();

        await foreach (var unclaimed in data.Unclaimed.WithCancellation(cancel))
        {
            dbContext.Holds.RemoveRange(unclaimed.Holds);
            dbContext.Wants.RemoveRange(unclaimed.Wants);

            dbContext.Unclaimed.Remove(unclaimed);
        }

        _loadProgress.AddProgress();

        await foreach (var bin in data.Bins.WithCancellation(cancel))
        {
            var binCards = bin.Boxes.SelectMany(b => b.Holds);

            dbContext.Holds.RemoveRange(binCards);
            dbContext.Boxes.RemoveRange(bin.Boxes);
            dbContext.Bins.Remove(bin);
        }

        _loadProgress.AddProgress();

        await foreach (var transaction in data.Transactions.WithCancellation(cancel))
        {
            dbContext.Changes.RemoveRange(transaction.Changes);
            dbContext.Transactions.Remove(transaction);
        }

        _loadProgress.AddProgress();

        await foreach (var suggestion in data.Suggestions.WithCancellation(cancel))
        {
            dbContext.Suggestions.Remove(suggestion);
        }

        _loadProgress.AddProgress();

        await dbContext.SaveChangesAsync(cancel);

        _loadProgress.AddProgress();
    }
}
