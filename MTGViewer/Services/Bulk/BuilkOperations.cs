using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services.Internal;

namespace MTGViewer.Services;

public class BulkOperations
{
    private readonly PageSizes _pageSizes;

    private readonly CardDbContext _dbContext;
    private readonly MTGFetchService _fetch;

    private readonly UserManager<CardUser> _userManager;
    private readonly string _tempPassword;

    public BulkOperations(
        IConfiguration config, 
        PageSizes pageSizes,
        CardDbContext dbContext,
        MTGFetchService fetch,
        UserManager<CardUser> userManager)
    {
        _pageSizes = pageSizes;

        _dbContext = dbContext;
        _fetch = fetch;

        var seedOptions = new SeedSettings();
        config.GetSection(nameof(SeedSettings)).Bind(seedOptions);

        _userManager = userManager;
        _tempPassword = seedOptions.Password;
    }


    public async ValueTask<int> GetTotalPagesAsync(CancellationToken cancel = default)
    {
        return await CardStream
            .DbSetCounts(_dbContext)
            .Select(count => count / _pageSizes.Limit)
            .Prepend(1)
            .MaxAsync(cancel);
    }


    public CardStream GetCardStream(DataScope scope, int? pageIndex = null)
    {
        return scope switch
        {
            DataScope.Full => CardStream.Create(_dbContext, _userManager),
            DataScope.Paged => CardStream.Create(_dbContext, _pageSizes.Limit, pageIndex),
            _ => CardStream.Create(_dbContext)
        };
    }


    public async Task SeedAsync(CardStream data, CancellationToken cancel = default)
    {
        await AddRangeAsync(data.Cards, _dbContext.Cards, cancel);
        await AddRangeAsync(data.Refs, _dbContext.Users, cancel);

        await AddRangeAsync(data.Bins, _dbContext.Bins, cancel);

        await AddRangeAsync(data.Decks, _dbContext.Decks, cancel);
        await AddRangeAsync(data.Unclaimed, _dbContext.Unclaimed, cancel);

        await AddRangeAsync(data.Transactions, _dbContext.Transactions, cancel);
        await AddRangeAsync(data.Suggestions, _dbContext.Suggestions, cancel);

        await _dbContext.SaveChangesAsync(cancel);

        await foreach (var user in data.Users.WithCancellation(cancel))
        {
            var result = await AddUserAsync(user, cancel);
            if (!result.Succeeded)
            {
                string errors = string.Join(',', result.Errors);
                throw new InvalidOperationException(errors);
            }
        }
    }


    private static async ValueTask AddRangeAsync<TEntity>(
        IAsyncEnumerable<TEntity> data, 
        DbSet<TEntity> dbSet,
        CancellationToken cancel) where TEntity : class
    {
        await foreach (var entity in data.WithCancellation(cancel))
        {
            dbSet.Add(entity);
        }
    }


    private async ValueTask<IdentityResult> AddUserAsync(CardUser user, CancellationToken cancel)
    {
        var created = _tempPassword != default
            ? await _userManager.CreateAsync(user, _tempPassword)
            : await _userManager.CreateAsync(user);
        
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


    public async Task SeedAsync(CardData data, CancellationToken cancel = default)
    {
        _dbContext.Users.AddRange(data.Refs);
        _dbContext.Cards.AddRange(data.Cards);

        _dbContext.Bins.AddRange(data.Bins);
        _dbContext.Decks.AddRange(data.Decks);

        _dbContext.Suggestions.AddRange(data.Suggestions);
        _dbContext.Trades.AddRange(data.Trades);

        await _dbContext.SaveChangesAsync(cancel);

        foreach (var user in data.Users)
        {
            await AddUserAsync(user, cancel);
        }
    }


    public async Task MergeAsync(CardData data, CancellationToken cancel = default)
    {
        // guarantee that multiple iters on data will return same obj ref
        var newCards = await GetNewCardsAsync(data, cancel);
        var existingCards = data.Cards.Except(newCards);

        var unclaimed = data.Decks
            .Select(d => (Unclaimed)d)
            .Concat(data.Unclaimed);

        var transaction = MergeTransaction(data.Bins, unclaimed);

        // TODO: merge to existing bins, boxes, and decks
        // merging may have issue with card amount/want conflicts

        if (!newCards.Any() && transaction is null)
        {
            throw new ArgumentException("Card data has no valid data", nameof(data));
        }

        _dbContext.Cards.AttachRange(existingCards);
        _dbContext.Cards.AddRange(newCards);

        _dbContext.Bins.AddRange(data.Bins);
        _dbContext.Unclaimed.AddRange(unclaimed);

        if (transaction is not null)
        {
            _dbContext.Transactions.Add(transaction);
        }

        await _dbContext.SaveChangesAsync(cancel);
    }


    private async Task<IReadOnlyList<Card>> GetNewCardsAsync(
        CardData data,
        CancellationToken cancel)
    {
        var dbCardIds = await ExistingCardsAsync(data, cancel);

        // existing cards will be left unmodified, so they don't 
        // need to be validated

        var newMultiIds = data.Cards
            .ExceptBy(dbCardIds, c => c.Id)
            .Where(c => c.MultiverseId is not null)
            .Select(c => c.MultiverseId);

        var validCards = await ValidatedCardsAsync(newMultiIds, cancel);

        var cardPairs = validCards
            .Join(data.Cards,
                v => v.Id, c => c.Id,
                (valid, card) => (card, valid));

        var newCards = new List<Card>();

        foreach (var (card, valid) in cardPairs)
        {
            _dbContext.Entry(card).CurrentValues.SetValues(valid);
            newCards.Add(card);
        }

        return newCards;
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
        IEnumerable<Bin> bins, 
        IEnumerable<Unclaimed> unclaimed)
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
    

    private async Task<IReadOnlyList<Card>> ValidatedCardsAsync(
        IEnumerable<string> multiIds, 
        CancellationToken cancel)
    {
        const int limit = MTGFetchService.Limit;

        cancel.ThrowIfCancellationRequested();

        var cards = new List<Card>();

        foreach (var multiChunk in multiIds.Chunk(limit))
        {
            if (!multiChunk.Any())
            {
                continue;
            }

            var multiArg = string.Join(MTGFetchService.Or, multiChunk);

            var validated = await _fetch
                .Where(c => c.MultiverseId, multiArg)
                .Where(c => c.PageSize, limit)
                .SearchAsync();

            cancel.ThrowIfCancellationRequested();

            cards.AddRange(validated);
        }

        return cards;
    }


    public async Task MergeAsync(
        IReadOnlyDictionary<string, int> multiAdditions,
        CancellationToken cancel)
    {
        if (!multiAdditions.Any())
        {
            return;
        }

        var multiIds = multiAdditions.Keys
            .ToAsyncEnumerable();

        var allCards = await _dbContext.Cards
            .AsAsyncEnumerable()
            .Join( multiIds,
                c => c.MultiverseId,
                mid => mid,
                (card, _) => card)
            .ToListAsync(cancel);

        var newMultiIds = multiAdditions.Keys
            .Except(allCards.Select(c => c.MultiverseId))
            .ToList();

        if (newMultiIds.Any())
        {
            var newCards = await ValidatedCardsAsync(newMultiIds, cancel);

            _dbContext.Cards.AddRange(newCards);
            allCards.AddRange(newCards);
        }

        var requests = allCards
            .IntersectBy(multiAdditions.Keys, c => c.MultiverseId)
            .Select(card => 
                new CardRequest(card, multiAdditions[card.MultiverseId]));

        await _dbContext.AddCardsAsync(requests, cancel);

        await _dbContext.SaveChangesAsync(cancel);
    }
}
