using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using MtgViewer.Data;
using MtgViewer.Services.Search;

namespace MtgViewer.Services.Infrastructure;

public class MergeHandler
{
    private readonly PageSize _pageSize;
    private readonly LoadingProgress _loadProgress;

    // treat each write function as a unit of work
    // read functions can use the same db context
    private readonly IDbContextFactory<CardDbContext> _dbFactory;
    private readonly IMtgQuery _mtgQuery;

    public MergeHandler(
        PageSize pageSize,
        LoadingProgress loadProgress,
        IDbContextFactory<CardDbContext> dbFactory,
        IMtgQuery mtgQuery)
    {
        _pageSize = pageSize;
        _loadProgress = loadProgress;
        _dbFactory = dbFactory;
        _mtgQuery = mtgQuery;
    }

    public async Task MergeAsync(CardData data, CancellationToken cancel = default)
    {
        _loadProgress.Ticks = 2;

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        bool hasNewCards = await MergeCardsAsync(dbContext, data, cancel);

        var unclaimed = data.Decks
            .Select(d => (Unclaimed)d)
            .Concat(data.Unclaimed)
            .ToList();

        var transaction = MergeTransaction(data.Bins, unclaimed);

        if ((transaction, hasNewCards, unclaimed.Any())
            is (null, false, false))
        {
            return;
        }

        // TODO: merge to existing bins, boxes, and decks
        // merging may have issue with card hold/want conflicts

        dbContext.Bins.AddRange(data.Bins);
        dbContext.Unclaimed.AddRange(unclaimed);

        if (transaction is not null)
        {
            dbContext.Transactions.Add(transaction);
        }

        // keep eye on, possible memory issues?

        await dbContext.UpdateBoxesAsync(cancel);

        _loadProgress.AddProgress();

        await dbContext.SaveChangesAsync(cancel);

        _loadProgress.AddProgress();
    }

    private async Task<bool> MergeCardsAsync(
        CardDbContext dbContext,
        CardData data,
        CancellationToken cancel)
    {
        var dbCards = await ExistingCardsAsync(dbContext, data, _pageSize.Limit, cancel);

        var newMultiverseIds = data.Cards
            .Select(c => c.MultiverseId)
            .Union(data.CardIds
                .Select(c => c.MultiverseId))

            .Except(dbCards
                .Select(c => c.MultiverseId));

        var validCards = _mtgQuery.CollectionAsync(newMultiverseIds, cancel);

        // existing cards will be left unmodified, so they don't
        // need to be validated

        var dataCardTable = data.Cards.ToDictionary(c => c.Id);

        bool hasNewCards = false;

        await foreach (var card in validCards)
        {
            hasNewCards = true;

            if (dataCardTable.TryGetValue(card.Id, out var conflict))
            {
                var newEntry = dbContext.Cards.Add(conflict);

                newEntry.CurrentValues.SetValues(card);
            }
            else
            {
                dbContext.Cards.Add(card);
            }
        }

        var missingCards = dataCardTable.Values
            .UnionBy(dbCards, c => c.Id)
            .ExceptBy(dbContext.Cards.Local
                .Select(c => c.Id), c => c.Id);

        dbContext.Cards.AttachRange(missingCards);

        return hasNewCards;
    }

    private static async Task<IReadOnlyList<Card>> ExistingCardsAsync(
        CardDbContext dbContext,
        CardData data,
        int limit,
        CancellationToken cancel)
    {
        if (data.Cards.Count + data.CardIds.Count < limit)
        {
            string[] cardIds = data.Cards
                .Select(c => c.Id)
                .Union(data.CardIds
                    .Select(c => c.Id))
                .ToArray();

            return await dbContext.Cards
                .Where(c => cardIds.Contains(c.Id))
                .AsNoTracking()
                .ToListAsync(cancel);
        }
        else
        {
            var cardIds = data.Cards
                .Select(c => c.Id)
                .Union(data.CardIds
                    .Select(c => c.Id))
                .ToAsyncEnumerable();

            return await dbContext.Cards
                .AsNoTracking()
                .AsAsyncEnumerable()
                .Join(cardIds,
                    c => c.Id, cid => cid,
                    (card, _) => card)
                .ToListAsync(cancel);
        }
    }

    private static Transaction? MergeTransaction(
        IReadOnlyList<Bin> bins,
        IReadOnlyList<Unclaimed> unclaimed)
    {
        var boxChanges = bins
            .SelectMany(b => b.Boxes)
            .SelectMany(b => b.Holds,
                (box, h) => new Change
                {
                    CardId = h.CardId,
                    To = box,
                    Copies = h.Copies
                });

        var deckChanges = unclaimed
            .SelectMany(u => u.Holds,
                (unclaimed, h) => new Change
                {
                    CardId = h.CardId,
                    To = unclaimed,
                    Copies = h.Copies
                });

        var changes = boxChanges
            .Concat(deckChanges)
            .ToList();

        if (!changes.Any())
        {
            return null;
        }

        return new Transaction
        {
            Changes = changes
        };
    }

    public async Task MergeAsync(
        IReadOnlyDictionary<string, int> multiverseAdditions,
        CancellationToken cancel = default)
    {
        if (!multiverseAdditions.Any())
        {
            return;
        }

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        var dbCards = await ExistingCardsAsync(dbContext, multiverseAdditions.Keys, _pageSize.Limit, cancel);

        var allCards = dbCards;

        _loadProgress.Ticks = 2;

        var newMultiverse = multiverseAdditions.Keys
            .Except(dbCards.Select(c => c.MultiverseId))
            .ToList();

        if (newMultiverse.Any())
        {
            var newCards = _mtgQuery.CollectionAsync(newMultiverse, cancel);

            await foreach (var card in newCards)
            {
                allCards.Add(card);
                dbContext.Cards.Add(card);
            }
        }

        var requests = allCards
            .Join(multiverseAdditions,
                c => c.MultiverseId, kv => kv.Key,
                (card, kv) => new CardRequest(card, kv.Value));

        await dbContext.AddCardsAsync(requests, cancel);

        _loadProgress.AddProgress();

        await dbContext.SaveChangesAsync(cancel);

        _loadProgress.AddProgress();
    }

    private static async Task<List<Card>> ExistingCardsAsync(
        CardDbContext dbContext,
        IEnumerable<string> multiverseIds,
        int limit,
        CancellationToken cancel)
    {
        if (multiverseIds.Count() < limit)
        {
            return await dbContext.Cards
                .Where(c => multiverseIds.Contains(c.MultiverseId))
                .ToListAsync(cancel);
        }
        else
        {
            var asyncMultiverse = multiverseIds
                .ToAsyncEnumerable();

            return await dbContext.Cards
                .AsAsyncEnumerable()
                .Join(asyncMultiverse,
                    c => c.MultiverseId,
                    mid => mid,
                    (card, _) => card)
                .ToListAsync(cancel);
        }
    }
}
