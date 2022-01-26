using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using CsvHelper;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;

namespace MTGViewer.Services;


public class FileCardStorage
{
    private readonly string _defaultFilename;
    private readonly PageSizes _pageSizes;

    private readonly IDbContextFactory<CardDbContext> _dbFactory;
    private readonly TreasuryHandler _treasuryHandler;

    private readonly MTGFetchService _fetch;

    private readonly UserManager<CardUser> _userManager;
    private readonly string _tempPassword; // TODO: change, see below for impl

    public FileCardStorage(
        IConfiguration config, 
        PageSizes pageSizes,
        IDbContextFactory<CardDbContext> dbFactory,
        TreasuryHandler treasuryHandler,
        MTGFetchService fetch,
        UserManager<CardUser> userManager)
    {
        var filename = config.GetValue("JsonPath", "cards");

        _defaultFilename = Path.ChangeExtension(filename, ".json");
        _pageSizes = pageSizes;

        _dbFactory = dbFactory;
        _treasuryHandler = treasuryHandler;

        _fetch = fetch;

        var seedOptions = new SeedSettings();
        config.GetSection(nameof(SeedSettings)).Bind(seedOptions);

        _userManager = userManager;
        _tempPassword = seedOptions.Password;
    }



    public async ValueTask<int> GetTotalPagesAsync(CancellationToken cancel = default)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        return await CardStream.DbSetCountsAsync(dbContext, cancel)
            .Select(count => count / _pageSizes.Limit)
            .Prepend(1)
            .MaxAsync(cancel);
    }


    public async Task<byte[]> GetBackupStreamAsync(int? page = null, CancellationToken cancel = default)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        var data = CardStream.Create(dbContext, _pageSizes.Limit, page);

        var serializeOptions = new JsonSerializerOptions 
        {
            ReferenceHandler = ReferenceHandler.Preserve
        };

        await using var utf8Stream = new MemoryStream(); // copy all data to memory, keep eye on

        await JsonSerializer.SerializeAsync(utf8Stream, data, serializeOptions, cancel);

        return utf8Stream.ToArray();
    }


    public async Task WriteBackupAsync(string? path = default, CancellationToken cancel = default)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);
        await using var writer = File.Create(path);

        var data = CardStream.Create(dbContext, _userManager);

        var serializeOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            WriteIndented = true
        };

        await JsonSerializer.SerializeAsync(writer, data, serializeOptions, cancel);
    }



    #region Json Seed

    public async Task JsonSeedAsync(string? path = default, CancellationToken cancel = default)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

        await using var reader = File.OpenRead(path);

        var deserializeOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            PropertyNameCaseInsensitive = true 
        };

        var data = await JsonSerializer.DeserializeAsync<CardStream>(reader, deserializeOptions, cancel);

        if (data is null)
        {
            throw new ArgumentException(nameof(path));
        }

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        await SeedAsync(dbContext, data, cancel);

        await dbContext.SaveChangesAsync(cancel);

        // TODO: generate secure temp passwords, and send emails to users

        var results = await data.Users
            .SelectAwaitWithCancellation( AddUserAsync )
            .ToListAsync(cancel);

        if (results.Any(r => !r.Succeeded))
        {
            var errors = string.Join(',', results.SelectMany(r => r.Errors));

            throw new InvalidOperationException(errors);
        }
    }


    private static async Task SeedAsync(CardDbContext dbContext, CardStream data, CancellationToken cancel)
    {
        await AddRangeAsync(data.Cards, dbContext.Cards, cancel);
        await AddRangeAsync(data.Refs, dbContext.Users, cancel);

        await AddRangeAsync(data.Bins, dbContext.Bins, cancel);

        await AddRangeAsync(data.Decks, dbContext.Decks, cancel);
        await AddRangeAsync(data.Unclaimed, dbContext.Unclaimed, cancel);

        await AddRangeAsync(data.Transactions, dbContext.Transactions, cancel);
        await AddRangeAsync(data.Suggestions, dbContext.Suggestions, cancel);
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

    #endregion



    #region Json Add

    public async Task JsonAddAsync(Stream jsonStream, CancellationToken cancel = default)
    {
        var deserializeOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            PropertyNameCaseInsensitive = true
        };

        var data = await JsonSerializer.DeserializeAsync<CardData>(jsonStream, deserializeOptions, cancel);

        if (data is null)
        {
            throw new ArgumentException(nameof(jsonStream));
        }

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);
        
        await MergeAsync(dbContext, data, cancel);

        await dbContext.SaveChangesAsync(cancel);
    }


    private async Task MergeAsync(CardDbContext dbContext, CardData data, CancellationToken cancel)
    {
        // guarantee that multiple iters on data will return same obj ref
        var newCards = await GetNewCardsAsync(dbContext, data, cancel);
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

        dbContext.Cards.AttachRange(existingCards);
        dbContext.Cards.AddRange(newCards);

        dbContext.Bins.AddRange(data.Bins);
        dbContext.Unclaimed.AddRange(unclaimed);

        if (transaction is not null)
        {
            dbContext.Transactions.Add(transaction);
        }
    }


    private async Task<IReadOnlyList<Card>> GetNewCardsAsync(
        CardDbContext dbContext,
        CardData data,
        CancellationToken cancel)
    {
        var dbCardIds = await ExistingCardsAsync(dbContext, data, cancel);

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
            dbContext.Entry(card).CurrentValues.SetValues(valid);
            newCards.Add(card);
        }

        return newCards;
    }


    private Task<List<string>> ExistingCardsAsync(
        CardDbContext dbContext,
        CardData data,
        CancellationToken cancel)
    {
        if (data.Cards.Count < _pageSizes.Limit)
        {
            var cardIds = data.Cards
                .Select(c => c.Id)
                .ToArray();

            return dbContext.Cards
                .Select(c => c.Id)
                .Where(cid => cardIds.Contains(cid))
                .ToListAsync(cancel);
        }
        else
        {
            var cardIds = data.Cards
                .Select(c => c.Id)
                .ToAsyncEnumerable();

            return dbContext.Cards
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

    #endregion



    private async Task<IReadOnlyList<Card>> ValidatedCardsAsync(
        IEnumerable<string> multiIds, 
        CancellationToken cancel)
    {
        const int limit = MTGFetchService.Limit;
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



    #region Csv Add

    private sealed class CsvCard
    {
        public string Name { get; set; } = string.Empty;
        public string MultiverseID { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }


    public async Task CsvAddAsync(Stream csvStream, CancellationToken cancel = default)
    {
        using var readStream = new StreamReader(csvStream);
        using var csv = new CsvReader(readStream, CultureInfo.InvariantCulture);

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        await MergeAsync(csv, dbContext, cancel);

        await dbContext.SaveChangesAsync(cancel);
    }


    private async Task MergeAsync(
        CsvReader csv, 
        CardDbContext dbContext, 
        CancellationToken cancel)
    {
        var csvAdditions = await CsvAdditionsAsync(csv, cancel);

        var allCards = await MergeCardsAsync(dbContext, csvAdditions.Keys, cancel);

        await AddAmountsAsync(dbContext, allCards, csvAdditions, cancel);
    }


    private Task<Dictionary<string, int>> CsvAdditionsAsync(
        CsvReader csv, 
        CancellationToken cancel)
    {
        return csv
            .GetRecordsAsync<CsvCard>(cancel)
            .Where(cc => cc.Quantity > 0)

            .GroupByAwaitWithCancellation(
                MultiverseIdAsync,
                async (multiverseId, ccs, cnl) => 
                    (multiverseId, quantity: await ccs.SumAsync(cc => cc.Quantity, cnl)))

            .ToDictionaryAsync(
                cc => cc.multiverseId, cc => cc.quantity, cancel)
            .AsTask();

        ValueTask<string> MultiverseIdAsync(CsvCard card, CancellationToken _)
        {
            return ValueTask.FromResult(card.MultiverseID);
        }
    }


    private async Task<IReadOnlyList<Card>> MergeCardsAsync(
        CardDbContext dbContext,
        IReadOnlyCollection<string> multiIds, 
        CancellationToken cancel)
    {
        if (!multiIds.Any())
        {
            return Array.Empty<Card>();
        }

        var allCards = await dbContext.Cards
            .AsAsyncEnumerable()
            .Join( multiIds.ToAsyncEnumerable(),
                c => c.MultiverseId,
                mid => mid,
                (card, _) => card)
            .ToListAsync(cancel);

        var newMultiIds = multiIds
            .Except(allCards.Select(c => c.MultiverseId));

        var newCards = await ValidatedCardsAsync(newMultiIds, cancel);

        dbContext.Cards.AddRange(newCards);
        allCards.AddRange(newCards);

        return allCards;
    }


    private async Task AddAmountsAsync(
        CardDbContext dbContext,
        IReadOnlyList<Card> cards,
        IReadOnlyDictionary<string, int> multiAdditions,
        CancellationToken cancel)
    {
        if (!cards.Any())
        {
            return;
        }

        var requests = cards
            .IntersectBy(multiAdditions.Keys, c => c.MultiverseId)
            .Select(card => 
                new CardRequest(card, multiAdditions[card.MultiverseId]));

        await _treasuryHandler.AddAsync(dbContext, requests, cancel);
    }

    #endregion
}