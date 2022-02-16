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


    // public async Task SeedAsync(CardStream data, CancellationToken cancel = default)
    // {
    //     await AddRangeAsync(data.Cards, _dbContext.Cards, cancel);
    //     await AddRangeAsync(data.Refs, _dbContext.Users, cancel);

    //     await AddRangeAsync(data.Bins, _dbContext.Bins, cancel);

    //     await AddRangeAsync(data.Decks, _dbContext.Decks, cancel);
    //     await AddRangeAsync(data.Unclaimed, _dbContext.Unclaimed, cancel);

    //     await AddRangeAsync(data.Transactions, _dbContext.Transactions, cancel);
    //     await AddRangeAsync(data.Suggestions, _dbContext.Suggestions, cancel);

    //     await _dbContext.SaveChangesAsync(cancel);

    //     await foreach (var user in data.Users.WithCancellation(cancel))
    //     {
    //         var result = await AddUserAsync(user, cancel);
    //         if (!result.Succeeded)
    //         {
    //             string errors = string.Join(',', result.Errors);
    //             throw new InvalidOperationException(errors);
    //         }
    //     }
    // }


    // private static async ValueTask AddRangeAsync<TEntity>(
    //     IAsyncEnumerable<TEntity> data, 
    //     DbSet<TEntity> dbSet,
    //     CancellationToken cancel) where TEntity : class
    // {
    //     await foreach (var entity in data.WithCancellation(cancel))
    //     {
    //         dbSet.Add(entity);
    //     }
    // }


    public async Task SeedAsync(CardData data, CancellationToken cancel = default)
    {
        _dbContext.Users.AddRange(data.Refs);
        _dbContext.Cards.AddRange(data.Cards);

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
        var dbCardIds = await ExistingCardsAsync(data, cancel);

        // existing cards will be left unmodified, so they don't 
        // need to be validated

        var newMultiIds = data.Cards
            .ExceptBy(dbCardIds, c => c.Id)
            .Where(c => c.MultiverseId is not null)
            .Select(c => c.MultiverseId);

        var validCards = await _mtgQuery.CollectionAsync(newMultiIds, cancel);

        var cardPairs = validCards
            .Join(data.Cards,
                v => v.Id, c => c.Id,
                (valid, card) => (card, valid));

        bool anyNewCards = false;

        foreach (var (card, valid) in cardPairs)
        {
            var addEntry = _dbContext.Cards.Add(card);

            addEntry.CurrentValues.SetValues(valid);

            anyNewCards = true;
        }

        var existingCards = data.Cards.Except(_dbContext.Cards.Local);

        _dbContext.Cards.AttachRange(existingCards);

        return anyNewCards;
    }


    private Task<List<string>> ExistingCardsAsync(
        CardData data,
        CancellationToken cancel)
    {
        if (data.Cards.Count < _pageSizes.Limit)
        {
            var cardIds = data.Cards
                .Select(c => c.Id)
                .ToArray();

            return _dbContext.Cards
                .Select(c => c.Id)
                .Where(cid => cardIds.Contains(cid))
                .ToListAsync(cancel);
        }
        else
        {
            var cardIds = data.Cards
                .Select(c => c.Id)
                .ToAsyncEnumerable();

            return _dbContext.Cards
                .Select(c => c.Id)
                .AsAsyncEnumerable()
                .Intersect(cardIds)
                .ToListAsync(cancel)
                .AsTask();
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
                    Card = a.Card,
                    To = box,
                    Amount = a.NumCopies
                });

        var deckChanges = unclaimed
            .SelectMany(u => u.Cards,
                (unclaimed, a) => new Change
                {
                    Card = a.Card,
                    To = unclaimed,
                    Amount = a.NumCopies
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
        IReadOnlyDictionary<string, int> multiAdditions,
        CancellationToken cancel = default)
    {
        if (!multiAdditions.Any())
        {
            return;
        }

        var dbCards = await ExistingCardsAsync(multiAdditions.Keys, cancel);

        var newMultiverse = multiAdditions.Keys
            .Except(dbCards.Select(c => c.MultiverseId))
            .ToList();

        var allCards = dbCards;

        _loadProgress.Ticks = 2;

        if (newMultiverse.Any())
        {
            var newCards = await _mtgQuery.CollectionAsync(newMultiverse, cancel);

            allCards.AddRange(newCards);
            _dbContext.Cards.AddRange(newCards);
        }

        var requests = allCards
            .IntersectBy(multiAdditions.Keys, c => c.MultiverseId)
            .Select(card => 
                new CardRequest(card, multiAdditions[card.MultiverseId]));

        await _dbContext.AddCardsAsync(requests, cancel);

        _loadProgress.AddProgress();

        await _dbContext.SaveChangesAsync(cancel);

        _loadProgress.AddProgress();
    }


    private Task<List<Card>> ExistingCardsAsync(
        IEnumerable<string> multiverseIds, 
        CancellationToken cancel)
    {
        if (multiverseIds.Count() < _pageSizes.Limit)
        {
            return _dbContext.Cards
                .Where(c => multiverseIds.Contains(c.MultiverseId))
                .ToListAsync(cancel);
        }
        else
        {
            var asyncMultiverse = multiverseIds
                .ToAsyncEnumerable();

            return _dbContext.Cards
                .AsAsyncEnumerable()
                .Join( asyncMultiverse,
                    c => c.MultiverseId,
                    mid => mid,
                    (card, _) => card)
                .ToListAsync(cancel)
                .AsTask();
        }
    }


    public async Task ResetAsync(CancellationToken cancel = default)
    {
        var data = GetDefaultStream();

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
