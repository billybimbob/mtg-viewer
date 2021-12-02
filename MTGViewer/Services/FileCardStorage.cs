using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

    private readonly CardDbContext _dbContext;
    private readonly ITreasuryQuery _treasuryQuery;
    private readonly MTGFetchService _fetch;

    private readonly UserManager<CardUser> _userManager;
    private readonly string _tempPassword; // TODO: change, see below for impl

    public FileCardStorage(
        IConfiguration config, 
        PageSizes pageSizes,
        CardDbContext dbContext, 
        ITreasuryQuery treasuryQuery,
        MTGFetchService fetch,
        UserManager<CardUser> userManager)
    {
        var filename = config.GetValue("JsonPath", "cards");

        _defaultFilename = Path.ChangeExtension(filename, ".json");
        _pageSize = pageSizes.Limit;

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
        return CardData.TotalPagesAsync(_dbContext, _pageSize, cancel);
    }


    public async Task WriteJsonAsync(
        string? path = default, CancellationToken cancel = default)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

        await using var writer = File.Create(path);

        var data = await CardData.CreateAsync(_dbContext, _userManager, cancel);

        var serialOptions = new JsonSerializerOptions { WriteIndented = true };

        await JsonSerializer.SerializeAsync(writer, data, serialOptions, cancel);
    }


    public async Task<byte[]> GetFileDataAsync(int? page = null, CancellationToken cancel = default)
    {
        var data = await CardData.CreateAsync(_dbContext, _pageSize, page, cancel);

        return JsonSerializer.SerializeToUtf8Bytes(data);
    }



    public async Task<bool> TryJsonSeedAsync(
        string? path = default, CancellationToken cancel = default)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

        try
        {
            await using var reader = File.OpenRead(path);

            var serialOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var data = await JsonSerializer.DeserializeAsync<CardData>(reader, serialOptions, cancel);

            if (data is null)
            {
                return false;
            }

            Seed(data);

            await _dbContext.SaveChangesAsync(cancel);

            _dbContext.ChangeTracker.Clear();

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


    private void Seed(CardData data)
    {
        _dbContext.Cards.AddRange(data.Cards);
        _dbContext.Users.AddRange(data.Refs);

        _dbContext.Bins.AddRange(data.Bins);
        _dbContext.Boxes.AddRange(data.Boxes);

        _dbContext.Decks.AddRange(data.Decks);
        _dbContext.Unclaimed.AddRange(data.Unclaimed);

        _dbContext.Amounts.AddRange(data.Amounts);

        _dbContext.Wants.AddRange(data.Wants);
        _dbContext.GiveBacks.AddRange(data.GiveBacks);

        _dbContext.Changes.AddRange(data.Changes);
        _dbContext.Transactions.AddRange(data.Transactions);

        _dbContext.Trades.AddRange(data.Trades);
        _dbContext.Suggestions.AddRange(data.Suggestions);
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

            var serialOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var data = await JsonSerializer.DeserializeAsync<CardData>(fileStream, serialOptions, cancel);

            if (data is null)
            {
                return false;
            }
            
            await MergeAsync(data, cancel);

            await _dbContext.SaveChangesAsync(cancel);

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


    private async Task MergeAsync(CardData data, CancellationToken cancel)
    {
        var allCardIds = await _dbContext.Cards
            .Select(c => c.Id)
            .ToListAsync(cancel);

        var cards = await NewCardsAsync(data, allCardIds, cancel);

        allCardIds.AddRange(cards.Select(c => c.Id));

        var bins = GetBinWithBoxAmounts(data, allCardIds);
        var unclaimed = GetDecksAsUnclaimed(data, allCardIds);

        var transaction = GetAddTranscation(bins, unclaimed);

        // TODO: merge to existing bins, boxes, and decks
        // merging may have issue with card amount/want conflicts

        _dbContext.Cards.AddRange(cards);

        _dbContext.Bins.AddRange(bins);
        _dbContext.Unclaimed.AddRange(unclaimed);

        _dbContext.Transactions.Add(transaction);
    }


    private async Task<IReadOnlyList<Card>> NewCardsAsync(
        CardData data, IReadOnlyList<string> dbCardIds, CancellationToken cancel)
    {
        var multiIds = data.Cards
            .ExceptBy(dbCardIds, c => c.Id)
            .Where(c => c.MultiverseId is not null)
            .Select(c => c.MultiverseId);

        return await ValidatedCardsAsync(multiIds, cancel);
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


    private IReadOnlyList<Bin> GetBinWithBoxAmounts(CardData data, IReadOnlyCollection<string> cardIds)
    {
        if (!data.Boxes.Any())
        {
            return Array.Empty<Bin>();
        }

        var bins = new List<Bin>();

        var boxTable = data.Boxes
            .ToLookup(b => b.BinId);

        var amountTable = data.Amounts
            .Join(cardIds,
                amt => amt.CardId, cid => cid,
                (amount, _) => amount)
            .ToLookup(a => a.LocationId);

        foreach (var bin in data.Bins)
        {
            var boxes = boxTable[bin.Id];

            foreach (var box in boxes)
            {
                var amounts = amountTable[box.Id];

                foreach (var amount in amounts)
                {
                    amount.Id = default;
                }

                box.Id = default;
                box.Cards.Clear();
                box.Cards.AddRange(amounts);
            }

            bin.Id = default;
            bin.Boxes.Clear();
            bin.Boxes.AddRange(boxes);

            bins.Add(bin);
        }

        return bins;
    }


    private IReadOnlyList<Unclaimed> GetDecksAsUnclaimed(CardData data, IReadOnlyList<string> cardIds)
    {
        if (!data.Decks.Any() && !data.Unclaimed.Any())
        {
            return Array.Empty<Unclaimed>();
        }

        var allUnclaimed = new List<Unclaimed>();

        var amountTable = data.Amounts
            .Join(cardIds,
                amt => amt.CardId, cid => cid,
                (amount, _) => amount)
            .ToLookup(ca => ca.LocationId);

        var wantTable = data.Wants
            .Join(cardIds,
                want => want.CardId, cid => cid,
                (want, _) => want)
            .ToLookup(w => w.LocationId);

        foreach (var unclaimed in data.Unclaimed)
        {
            var unclaimedCards = amountTable[unclaimed.Id];

            foreach (var amount in unclaimedCards)
            {
                amount.Id = default;
            }

            unclaimed.Id = default;
            unclaimed.Cards.Clear();
            unclaimed.Cards.AddRange(unclaimedCards);

            allUnclaimed.Add(unclaimed);
        }

        foreach (var deck in data.Decks)
        {
            var unclaimed = (Unclaimed) deck;

            var unclaimedCards = amountTable[deck.Id];
            var unclaimedWants = wantTable[deck.Id];

            foreach (var amount in unclaimedCards)
            {
                amount.Id = default;
            }

            foreach (var want in unclaimedWants)
            {
                want.Id = default;
            }

            unclaimed.Cards.Clear();
            unclaimed.Wants.Clear();

            unclaimed.Cards.AddRange(unclaimedCards);
            unclaimed.Wants.AddRange(unclaimedWants);

            allUnclaimed.Add(unclaimed);
        }

        return allUnclaimed;
    }


    private Transaction GetAddTranscation(
        IReadOnlyList<Bin> bins, 
        IReadOnlyList<Unclaimed> unclaimed)
    {
        var transaction = new Transaction();

        var boxChanges = bins
            .SelectMany(b => b.Boxes)
            .SelectMany(b => b.Cards,
                (box, a) => new Change
                {
                    CardId = a.CardId, // cardId should be known
                    To = box,
                    Amount = a.NumCopies
                });

        transaction.Changes.AddRange(boxChanges);

        var deckChanges = unclaimed
            .SelectMany(u => u.Cards,
                (unclaimed, a) => new Change
                {
                    CardId = a.CardId, // cardId should be known
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
            await using var fileStream = csvFile.OpenReadStream();

            using var readStream = new StreamReader(fileStream);
            var culture = System.Globalization.CultureInfo.InvariantCulture;

            using var csv = new CsvReader(readStream, culture);

            await MergeAsync(csv, cancel);

            await _dbContext.SaveChangesAsync(cancel);

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


    private async Task MergeAsync(CsvReader csv, CancellationToken cancel)
    {
        var cardMultis = await csv.GetRecordsAsync<CsvCard>(cancel)
            .Where(cc => cc.Quantity > 0)

            .GroupByAwaitWithCancellation(
                (cc, _) => ValueTask.FromResult(cc.MultiverseID),
                async (multiverseId, ccs, can) => 
                    (multiverseId, quantity: await ccs.SumAsync(cc => cc.Quantity, can)))

            .ToDictionaryAsync(
                cc => cc.multiverseId, cc => cc.quantity, cancel);

        var allCards = await MergeCardsAsync(cardMultis.Keys, cancel);

        await AddAmountsAsync(allCards, cardMultis, cancel);
    }


    private async Task<IReadOnlyList<Card>> MergeCardsAsync(
        IReadOnlyCollection<string> multiIds, 
        CancellationToken cancel)
    {
        if (!multiIds.Any())
        {
            return Array.Empty<Card>();
        }

        var allCards = await _dbContext.Cards
            .Where(c => multiIds.Contains(c.MultiverseId))
            .ToListAsync(cancel);

        var newMultiIds = multiIds
            .Except(allCards.Select(c => c.MultiverseId));

        var newCards = await ValidatedCardsAsync(newMultiIds, cancel);

        _dbContext.Cards.AddRange(newCards);
        allCards.AddRange(newCards);

        return allCards;
    }


    private async Task AddAmountsAsync(
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

        _dbContext.AttachResult(result);
        _dbContext.Changes.AttachRange(addChanges);
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

    public IReadOnlyList<Box> Boxes { get; set; } = Array.Empty<Box>();
    public IReadOnlyList<Bin> Bins { get; set; } = Array.Empty<Bin>();

    public IReadOnlyList<Amount> Amounts { get; set; } = Array.Empty<Amount>();
    public IReadOnlyList<Want> Wants { get; set; } = Array.Empty<Want>();
    public IReadOnlyList<GiveBack> GiveBacks { get; set; } = Array.Empty<GiveBack>();

    public IReadOnlyList<Change> Changes { get; set; } = Array.Empty<Change>();
    public IReadOnlyList<Transaction> Transactions { get; set; } = Array.Empty<Transaction>();

    public IReadOnlyList<Trade> Trades { get; set; } = Array.Empty<Trade>();
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
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(c => c.Id)
                .ToPagedListAsync(pageSize, page, cancel),

            Decks = await dbContext.Decks
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(d => d.Id)
                .ToPagedListAsync(pageSize, page, cancel),

            Unclaimed = await dbContext.Unclaimed
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(u => u.Id)
                .ToPagedListAsync(pageSize, page, cancel),

            Boxes = await dbContext.Boxes
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(b => b.Id)
                .ToPagedListAsync(pageSize, page, cancel),
            
            Bins = await dbContext.Bins
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(b => b.Id)
                .ToPagedListAsync(pageSize, page, cancel),

            Amounts = await dbContext.Amounts
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(a => a.Id)
                .ToPagedListAsync(pageSize, page, cancel),

            Wants = await dbContext.Wants
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(w => w.Id)
                .ToPagedListAsync(pageSize, page, cancel)
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
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(u => u.Id)
                .ToListAsync(cancel),

            Cards = await dbContext.Cards
                .Include(c => c.Colors)
                .Include(c => c.Types)
                .Include(c => c.Subtypes)
                .Include(c => c.Supertypes)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(c => c.Id)
                .ToListAsync(cancel),

            Decks = await dbContext.Decks
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(d => d.Id)
                .ToListAsync(cancel),

            Unclaimed = await dbContext.Unclaimed
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(u => u.Id)
                .ToListAsync(cancel),

            Boxes = await dbContext.Boxes
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(b => b.Id)
                .ToListAsync(cancel),
            
            Bins = await dbContext.Bins
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(b => b.Id)
                .ToListAsync(cancel),

            Amounts = await dbContext.Amounts
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(a => a.Id)
                .ToListAsync(cancel),

            Wants = await dbContext.Wants
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(w => w.Id)
                .ToListAsync(cancel),

            GiveBacks = await dbContext.GiveBacks
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(g => g.Id)
                .ToListAsync(cancel),

            Changes = await dbContext.Changes
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(c => c.Id)
                .ToListAsync(cancel),

            Transactions = await dbContext.Transactions
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(t => t.Id)
                .ToListAsync(cancel),

            Trades = await dbContext.Trades
                .AsNoTrackingWithIdentityResolution()
                .OrderBy(t => t.Id)
                .ToListAsync(cancel),

            Suggestions = await dbContext.Suggestions
                .AsNoTrackingWithIdentityResolution()
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

        yield return await dbContext.Boxes.CountAsync(cancel);

        yield return await dbContext.Bins.CountAsync(cancel);

        yield return await dbContext.Amounts.CountAsync(cancel);

        yield return await dbContext.Wants.CountAsync(cancel);
    }
}