using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services.Internal;

namespace MTGViewer.Services;

public class BulkOperations
{
    private readonly SeedSettings _seedSettings;
    private readonly PageSizes _pageSizes;
    private readonly LoadingProgress _loadProgress;

    // might want to use dbFactory, keep eye on
    private readonly CardDbContext _dbContext;
    private readonly IMTGQuery _mtgQuery;
    private readonly UserManager<CardUser> _userManager;


    public BulkOperations(
        IOptions<SeedSettings> seedOptions, 
        PageSizes pageSizes,
        LoadingProgress loadProgress,
        CardDbContext dbContext,
        IMTGQuery fetch,
        UserManager<CardUser> userManager)
    {
        _pageSizes = pageSizes;
        _seedSettings = seedOptions.Value;
        _loadProgress = loadProgress;

        _dbContext = dbContext;
        _mtgQuery = fetch;

        _userManager = userManager;
    }


    public CardStream GetDefaultStream() => CardStream.Default(_dbContext);

    public CardStream GetUserStream(string userId) => CardStream.User(_dbContext, userId);

    public CardStream GetTreasuryStream() => CardStream.Treasury(_dbContext);

    public CardStream GetSeedStream() => CardStream.All(_dbContext, _userManager);



    public async Task SeedAsync(CardData data, CancellationToken cancel = default)
    {
        _dbContext.Users.AddRange(data.Refs);
        _dbContext.Cards.AddRange(data.Cards);

        // ignore card ids, just assume it will be empty

        _dbContext.Bins.AddRange(data.Bins);
        _dbContext.Decks.AddRange(data.Decks);

        _dbContext.Suggestions.AddRange(data.Suggestions);
        _dbContext.Trades.AddRange(data.Trades);

        _loadProgress.Ticks = data.Users.Count + 1;

        await _dbContext.SaveChangesAsync(cancel);

        _loadProgress.AddProgress();

        foreach (var user in data.Users)
        {
            await AddUserAsync(user, cancel);

            _loadProgress.AddProgress();
        }
    }


    private async ValueTask<IdentityResult> AddUserAsync(CardUser user, CancellationToken cancel)
    {
        var created = string.IsNullOrWhiteSpace(_seedSettings.Password)
            ? await _userManager.CreateAsync(user)
            : await _userManager.CreateAsync(user, _seedSettings.Password);
        
        cancel.ThrowIfCancellationRequested();

        var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
        
        cancel.ThrowIfCancellationRequested();

        if (!providers.Any())
        {
            return created;
        }

        string token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        cancel.ThrowIfCancellationRequested();

        var confirmed = await _userManager.ConfirmEmailAsync(user, token);

        cancel.ThrowIfCancellationRequested();

        return confirmed;
    }


    public async Task MergeAsync(CardData data, CancellationToken cancel = default)
    {
        _loadProgress.Ticks = 2;

        var unclaimed = data.Decks
            .Select(d => (Unclaimed)d)
            .Concat(data.Unclaimed)
            .ToList();

        var transaction = MergeTransaction(data.Bins, unclaimed);

        bool hasNewCards = await MergeCardsAsync(data, cancel);

        if (transaction is null && !hasNewCards)
        {
            return;
        }

        // TODO: merge to existing bins, boxes, and decks
        // merging may have issue with card amount/want conflicts

        _dbContext.Bins.AddRange(data.Bins);
        _dbContext.Unclaimed.AddRange(unclaimed);

        if (transaction is not null)
        {
            _dbContext.Transactions.Add(transaction);
        }

        // keep eye on, possible memory issues?

        await _dbContext.UpdateBoxesAsync(cancel);

        _loadProgress.AddProgress();

        await _dbContext.SaveChangesAsync(cancel);

        _loadProgress.AddProgress();
    }


    private async Task<bool> MergeCardsAsync(CardData data, CancellationToken cancel)
    {
        var dbCards = await ExistingCardsAsync(data, cancel);

        var newMultiverseIds = data.Cards
            .Select(c => c.MultiverseId)
            .Union(data.CardIds
                .Select(c => c.MultiverseId))

            .Except(dbCards
                .Select(c => c.MultiverseId));

        var validCards = _mtgQuery
            .Collection(newMultiverseIds)
            .WithCancellation(cancel);

        // existing cards will be left unmodified, so they don't 
        // need to be validated

        var dataCardTable = data.Cards.ToDictionary(c => c.Id);

        bool hasNewCards = false;

        await foreach (var card in validCards)
        {
            hasNewCards = true;

            if (dataCardTable.TryGetValue(card.Id, out var conflict))
            {
                _dbContext.Cards
                    .Add(conflict).CurrentValues
                    .SetValues(card);
            }
            else
            {
                _dbContext.Cards.Add(card);
            }
        }

        var missingDbCards = dbCards
            .Where(c => !dataCardTable.ContainsKey(c.Id));

        _dbContext.Cards.AttachRange(missingDbCards);

        return hasNewCards;
    }


    private async ValueTask<List<Card>> ExistingCardsAsync(
        CardData data,
        CancellationToken cancel)
    {
        if (data.Cards.Count + data.CardIds.Count < _pageSizes.Limit)
        {
            var cardIds = data.Cards
                .Select(c => c.Id)
                .Union(data.CardIds
                    .Select(c => c.Id))
                .ToArray();

            return await _dbContext.Cards
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

            return await _dbContext.Cards
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
            .SelectMany(b => b.Cards,
                (box, a) => new Change
                {
                    CardId = a.CardId,
                    To = box,
                    Amount = a.Copies
                });

        var deckChanges = unclaimed
            .SelectMany(u => u.Cards,
                (unclaimed, a) => new Change
                {
                    CardId = a.CardId,
                    To = unclaimed,
                    Amount = a.Copies
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
        IDictionary<string, int> multiverseAdditions,
        CancellationToken cancel = default)
    {
        if (!multiverseAdditions.Any())
        {
            return;
        }
        
        multiverseAdditions = new SortedList<string, int>(multiverseAdditions);

        var dbCards = await ExistingCardsAsync(multiverseAdditions.Keys, cancel);

        var allCards = dbCards;

        _loadProgress.Ticks = 2;

        var newMultiverse = multiverseAdditions.Keys
            .Except(dbCards.Select(c => c.MultiverseId))
            .ToList();

        if (newMultiverse.Any())
        {
            var newCards = _mtgQuery
                .Collection(newMultiverse)
                .WithCancellation(cancel);

            await foreach (var card in newCards)
            {
                allCards.Add(card);
                _dbContext.Cards.Add(card);
            }
        }

        var requests = allCards
            .Join( multiverseAdditions,
                c => c.MultiverseId, kv => kv.Key,
                (card, kv) => new CardRequest(card, kv.Value));

        await _dbContext.AddCardsAsync(requests, cancel);

        _loadProgress.AddProgress();

        await _dbContext.SaveChangesAsync(cancel);

        _loadProgress.AddProgress();
    }


    private async ValueTask<List<Card>> ExistingCardsAsync(
        IEnumerable<string> multiverseIds, 
        CancellationToken cancel)
    {
        if (multiverseIds.Count() < _pageSizes.Limit)
        {
            return await _dbContext.Cards
                .Where(c => multiverseIds.Contains(c.MultiverseId))
                .ToListAsync(cancel);
        }
        else
        {
            var asyncMultiverse = multiverseIds
                .ToAsyncEnumerable();

            return await _dbContext.Cards
                .AsAsyncEnumerable()
                .Join( asyncMultiverse,
                    c => c.MultiverseId,
                    mid => mid,
                    (card, _) => card)
                .ToListAsync(cancel);
        }
    }


    public async Task ResetAsync(CancellationToken cancel = default)
    {
        var data = CardStream.Reset(_dbContext);

        _loadProgress.Ticks = 7;

        await foreach (var card in data.Cards.WithCancellation(cancel))
        {
            _dbContext.Cards.Remove(card);
        }

        _loadProgress.AddProgress();

        await foreach (var deck in data.Decks.WithCancellation(cancel))
        {
            _dbContext.Amounts.RemoveRange(deck.Cards);
            _dbContext.Wants.RemoveRange(deck.Wants);
            _dbContext.GiveBacks.RemoveRange(deck.GiveBacks);

            _dbContext.Decks.Remove(deck);
        }

        _loadProgress.AddProgress();

        await foreach (var unclaimed in data.Unclaimed.WithCancellation(cancel))
        {
            _dbContext.Amounts.RemoveRange(unclaimed.Cards);
            _dbContext.Wants.RemoveRange(unclaimed.Wants);
            
            _dbContext.Unclaimed.Remove(unclaimed);
        }

        _loadProgress.AddProgress();

        await foreach (var bin in data.Bins.WithCancellation(cancel))
        {
            var binCards = bin.Boxes.SelectMany(b => b.Cards);

            _dbContext.Amounts.RemoveRange(binCards);
            _dbContext.Boxes.RemoveRange(bin.Boxes);
            _dbContext.Bins.Remove(bin);
        }

        _loadProgress.AddProgress();

        await foreach (var transaction in data.Transactions.WithCancellation(cancel))
        {
            _dbContext.Changes.RemoveRange(transaction.Changes);
            _dbContext.Transactions.Remove(transaction);
        }

        _loadProgress.AddProgress();

        await foreach (var suggestion in data.Suggestions.WithCancellation(cancel))
        {
            _dbContext.Suggestions.Remove(suggestion);
        }

        _loadProgress.AddProgress();

        await _dbContext.SaveChangesAsync(cancel);

        _loadProgress.AddProgress();
    }
}
