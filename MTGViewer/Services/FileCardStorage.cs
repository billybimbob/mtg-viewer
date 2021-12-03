using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

using Microsoft.AspNetCore.Http;
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
    private readonly int _pageSize;

    private readonly IDbContextFactory<CardDbContext> _dbFactory;
    private readonly CardDbContext _dbContext;

    private readonly ITreasuryQuery _treasuryQuery;
    private readonly MTGFetchService _fetch;

    private readonly UserManager<CardUser> _userManager;
    private readonly string _tempPassword; // TODO: change, see below for impl

    public FileCardStorage(
        IConfiguration config, 
        PageSizes pageSizes,
        IDbContextFactory<CardDbContext> dbFactory,
        CardDbContext dbContext, 
        ITreasuryQuery treasuryQuery,
        MTGFetchService fetch,
        UserManager<CardUser> userManager)
    {
        var filename = config.GetValue("JsonPath", "cards");

        _defaultFilename = Path.ChangeExtension(filename, ".json");
        _pageSize = pageSizes.Limit;

        _dbFactory = dbFactory;
        _dbContext = dbContext;

        _treasuryQuery = treasuryQuery;
        _fetch = fetch;

        var seedOptions = new SeedSettings();
        config.GetSection(nameof(SeedSettings)).Bind(seedOptions);

        _tempPassword = seedOptions.Password;
        _userManager = userManager;
    }


    public Task<int> GetTotalPagesAsync(CancellationToken cancel = default)
    {
        // fine to not use factory here since the query is not tracked
        return CardData.TotalPagesAsync(_dbContext, _pageSize, cancel);
    }


    public async Task WriteJsonAsync(
        string? path = default, CancellationToken cancel = default)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);
        await using var writer = File.Create(path);

        var data = await CardData.CreateAsync(dbContext, _userManager, cancel);

        var serialOptions = new JsonSerializerOptions 
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            WriteIndented = true 
        };

        await JsonSerializer.SerializeAsync(writer, data, serialOptions, cancel);
    }


    public async Task<byte[]> GetFileDataAsync(int? page = null, CancellationToken cancel = default)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        var data = await CardData.CreateAsync(dbContext, _pageSize, page, cancel);

        return JsonSerializer.SerializeToUtf8Bytes(data);
    }



    public async Task<bool> TryJsonSeedAsync(
        string? path = default,
        CancellationToken cancel = default)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

        try
        {
            await using var reader = File.OpenRead(path);

            var serialOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                PropertyNameCaseInsensitive = true 
            };

            var data = await JsonSerializer.DeserializeAsync<CardData>(reader, serialOptions, cancel);

            if (data is null)
            {
                return false;
            }

            await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

            Seed(dbContext, data);

            await dbContext.SaveChangesAsync(cancel);

            // TODO: generate secure temp passwords, and send emails to users
            var results = await Task.WhenAll(
                data.Users.Select(u => _tempPassword != default
                    ? _userManager.CreateAsync(u, _tempPassword)
                    : _userManager.CreateAsync(u)));

            return results.All(r => r.Succeeded);

        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }


    private static void Seed(CardDbContext dbContext, CardData data)
    {
        dbContext.Cards.AddRange(data.Cards);
        dbContext.Users.AddRange(data.Refs);

        dbContext.Bins.AddRange(data.Bins);

        dbContext.Decks.AddRange(data.Decks);
        dbContext.Unclaimed.AddRange(data.Unclaimed);

        dbContext.Transactions.AddRange(data.Transactions);
        dbContext.Suggestions.AddRange(data.Suggestions);
    }


    public async Task<bool> TryJsonAddAsync(IFormFile jsonFile, CancellationToken cancel = default)
    {
        string ext = Path.GetExtension(jsonFile.FileName).ToLower();

        if (ext != ".json")
        {
            return false;
        }

        try
        {
            await using var fileStream = jsonFile.OpenReadStream();

            var serialOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                PropertyNameCaseInsensitive = true
            };

            var data = await JsonSerializer.DeserializeAsync<CardData>(fileStream, serialOptions, cancel);

            if (data is null)
            {
                return false;
            }

            await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);
            
            await MergeAsync(dbContext, data, cancel);

            await dbContext.SaveChangesAsync(cancel);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }


    private async Task MergeAsync(CardDbContext dbContext, CardData data, CancellationToken cancel)
    {
        var cards = await NewCardsAsync(dbContext, data, cancel);

        var bins = data.Bins;
        var unclaimed = GetDecksAsUnclaimed(data);

        var transaction = GetAddTranscation(bins, unclaimed);

        // TODO: merge to existing bins, boxes, and decks
        // merging may have issue with card amount/want conflicts

        dbContext.Cards.AddRange(cards);

        dbContext.Bins.AddRange(bins);
        dbContext.Unclaimed.AddRange(unclaimed);

        dbContext.Transactions.Add(transaction);
    }


    private async Task<IReadOnlyList<Card>> NewCardsAsync(
        CardDbContext dbContext,
        CardData data,
        CancellationToken cancel)
    {
        var dataCardIds = data.Cards
            .Select(c => c.Id)
            .ToArray();

        var dbCardIds = await dbContext.Cards
            .Select(c => c.Id)
            .Where(cid => dataCardIds.Contains(cid))
            .ToListAsync(cancel);

        var multiIds = data.Cards
            .ExceptBy(dbCardIds, c => c.Id)
            .Where(c => c.MultiverseId is not null)
            .Select(c => c.MultiverseId);

        var validated = await ValidatedCardsAsync(multiIds, cancel);

        var newCards = new List<Card>();

        var validatePairs = data.Cards
            .Join(validated,
                c => c.Id, v => v.Id,
                (card, valid) => (card, valid));

        foreach(var (card, valid) in validatePairs)
        {
            dbContext.Entry(card).CurrentValues.SetValues(valid);
            newCards.Add(card);
        }

        return newCards;
    }


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


    private static IReadOnlyList<Unclaimed> GetDecksAsUnclaimed(CardData data)
    {
        if (!data.Decks.Any() && !data.Unclaimed.Any())
        {
            return Array.Empty<Unclaimed>();
        }

        return data.Decks
            .Select(d => (Unclaimed)d)
            .Concat(data.Unclaimed)
            .ToList();
    }


    private static Transaction GetAddTranscation(
        IReadOnlyList<Bin> bins, 
        IReadOnlyList<Unclaimed> unclaimed)
    {
        var transaction = new Transaction();

        var boxChanges = bins
            .SelectMany(b => b.Boxes)
            .SelectMany(b => b.Cards,
                (box, a) => new Change
                {
                    Card = a.Card, // cardId should be known
                    To = box,
                    Amount = a.NumCopies
                });

        transaction.Changes.AddRange(boxChanges);

        var deckChanges = unclaimed
            .SelectMany(u => u.Cards,
                (unclaimed, a) => new Change
                {
                    Card = a.Card,
                    To = unclaimed,
                    Amount = a.NumCopies
                });

        transaction.Changes.AddRange(deckChanges);

        return transaction;
    }


    private sealed class CsvCard
    {
        public string Name { get; set; } = string.Empty;
        public string MultiverseID { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }


    public async Task<bool> TryCsvAddAsync(IFormFile csvFile, CancellationToken cancel = default)
    {
        string ext = Path.GetExtension(csvFile.FileName).ToLower();

        if (ext != ".csv")
        {
            return false;
        }

        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

            await using var fileStream = csvFile.OpenReadStream();
            using var readStream = new StreamReader(fileStream);
            using var csv = new CsvReader(readStream, CultureInfo.InvariantCulture);

            await MergeAsync(dbContext, csv, cancel);

            await dbContext.SaveChangesAsync(cancel);

            return true;
        }
        catch (CsvHelperException)
        {
            return false;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }


    private async Task MergeAsync(
        CardDbContext dbContext, 
        CsvReader csv, 
        CancellationToken cancel)
    {
        var cardMultis = await csv.GetRecordsAsync<CsvCard>(cancel)
            .Where(cc => cc.Quantity > 0)
            .GroupByAwaitWithCancellation(
                (cc, _) => ValueTask.FromResult(cc.MultiverseID),
                async (multiverseId, ccs, can) => 
                    (multiverseId, quantity: await ccs.SumAsync(cc => cc.Quantity, can)))

            .ToDictionaryAsync(
                cc => cc.multiverseId, cc => cc.quantity, cancel);

        var allCards = await MergeCardsAsync(dbContext, cardMultis.Keys, cancel);

        await AddAmountsAsync(dbContext, allCards, cardMultis, cancel);
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
            .Where(c => multiIds.Contains(c.MultiverseId))
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
        IReadOnlyDictionary<string, int> cardMultis,
        CancellationToken cancel)
    {
        if (!cards.Any())
        {
            return;
        }

        var requests = cards
            .IntersectBy(cardMultis.Keys, c => c.MultiverseId)
            .Select(card => 
                new CardRequest(card, cardMultis[card.MultiverseId]));

        var result = await _treasuryQuery.FindReturnAsync(requests, cancel);

        var (addTargets, oldCopies) = result;
        var newTransaction = new Transaction();

        var addChanges = addTargets
            .Select(a => new Change
            {
                Card = a.Card,
                To = a.Location,
                Amount = a.NumCopies - oldCopies.GetValueOrDefault(a.Id),
                Transaction = newTransaction
            });

        dbContext.AttachResult(result);
        dbContext.Changes.AttachRange(addChanges);
    }
}


// TODO: make reading use less memory
internal class CardData
{
    public IReadOnlyList<CardUser> Users { get; set; } = Array.Empty<CardUser>();
    public IReadOnlyList<UserRef> Refs { get; set; } = Array.Empty<UserRef>();

    public IReadOnlyList<Card> Cards { get; set; } = Array.Empty<Card>();

    public IReadOnlyList<Deck> Decks { get; set; } = Array.Empty<Deck>();
    public IReadOnlyList<Unclaimed> Unclaimed { get; set; } = Array.Empty<Unclaimed>();

    public IReadOnlyList<Bin> Bins { get; set; } = Array.Empty<Bin>();

    public IReadOnlyList<Transaction> Transactions { get; set; } = Array.Empty<Transaction>();
    public IReadOnlyList<Suggestion> Suggestions { get; set; } = Array.Empty<Suggestion>();


    public static async Task<CardData> CreateAsync(
        CardDbContext dbContext,
        int pageSize,
        int? page = default,
        CancellationToken cancel = default)
    {
        return new CardData
        {
            Cards = await dbContext.Cards
                .Include(c => c.Colors)
                .Include(c => c.Types)
                .Include(c => c.Subtypes)
                .Include(c => c.Supertypes)
                .AsSplitQuery()
                .OrderBy(c => c.Id)
                .ToPagedListAsync(pageSize, page, cancel),

            Decks = await dbContext.Decks
                // keep eye on, paging does not account for
                // the variable amount of Quantity entries
                .Include(d => d.Cards)
                .Include(d => d.Wants)
                .OrderBy(d => d.Id)
                .ToPagedListAsync(pageSize, page, cancel),

            Unclaimed = await dbContext.Unclaimed
                .Include(u => u.Cards)
                .Include(u => u.Wants)
                .OrderBy(u => u.Id)
                .ToPagedListAsync(pageSize, page, cancel),
            
            Bins = await dbContext.Bins
                .Include(b => b.Boxes)
                    .ThenInclude(b => b.Cards)
                .OrderBy(b => b.Id)
                .ToPagedListAsync(pageSize, page, cancel),
        };
    }


    public static async Task<CardData> CreateAsync(
        CardDbContext dbContext,
        UserManager<CardUser> userManager,
        CancellationToken cancel = default)
    { 
        return new CardData
        {
            Users = await userManager.Users
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(u => u.Id)
                .ToListAsync(cancel),

            Refs = await dbContext.Users
                .OrderBy(u => u.Id)
                .ToListAsync(cancel),

            Cards = await dbContext.Cards
                .Include(c => c.Colors)
                .Include(c => c.Types)
                .Include(c => c.Subtypes)
                .Include(c => c.Supertypes)
                .OrderBy(c => c.Id)
                .AsSplitQuery()
                .ToListAsync(cancel),

            Decks = await dbContext.Decks
                .Include(d => d.Cards)
                .Include(d => d.Wants)
                .Include(d => d.GiveBacks)
                .Include(d => d.TradesFrom)
                .Include(d => d.TradesTo)
                .OrderBy(d => d.Id)
                .AsSplitQuery()
                .ToListAsync(cancel),

            Unclaimed = await dbContext.Unclaimed
                .Include(u => u.Cards)
                .Include(u => u.Wants)
                .OrderBy(u => u.Id)
                .AsSplitQuery()
                .ToListAsync(cancel),
            
            Bins = await dbContext.Bins
                .Include(b => b.Boxes)
                    .ThenInclude(b => b.Cards)
                .OrderBy(b => b.Id)
                .AsSplitQuery()
                .ToListAsync(cancel),

            Transactions = await dbContext.Transactions
                .Include(t => t.Changes)
                .OrderBy(t => t.Id)
                .ToListAsync(cancel),

            Suggestions = await dbContext.Suggestions
                .OrderBy(s => s.Id)
                .ToListAsync(cancel)
        };
    }


    public static Task<int> TotalPagesAsync(
        CardDbContext dbContext,
        int pageSize, 
        CancellationToken cancel = default)
    {
        return DbSetCountsAsync(dbContext, cancel)
            .Select(count => count / pageSize)
            .Prepend(1)
            .MaxAsync(cancel)
            .AsTask();
    }


    private static async IAsyncEnumerable<int> DbSetCountsAsync(
        CardDbContext dbContext,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancel = default)
    {
        yield return await dbContext.Cards.CountAsync(cancel);

        yield return await dbContext.Decks.CountAsync(cancel);

        yield return await dbContext.Unclaimed.CountAsync(cancel);

        yield return await dbContext.Bins.CountAsync(cancel);
    }
}