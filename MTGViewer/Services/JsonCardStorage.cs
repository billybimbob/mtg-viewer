using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;

namespace MTGViewer.Services;

public readonly struct JsonStorageOptions
{
    public string Path { get; init; }

    public bool Seeding { get; init; }
}


public class JsonCardStorage
{
    private readonly string _defaultFilename;
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;

    private readonly string _tempPassword; // TODO: change, see below for impl

    public JsonCardStorage(
        IConfiguration config, 
        CardDbContext dbContext, 
        UserManager<CardUser> userManager)
    {
        var filename = config.GetValue("JsonPath", "cards");

        _defaultFilename = Path.ChangeExtension(filename, ".json");

        _dbContext = dbContext;
        _userManager = userManager;

        var seedOptions = new SeedSettings();
        config.GetSection(nameof(SeedSettings)).Bind(seedOptions);

        _tempPassword = seedOptions.Password;
    }


    public async Task WriteToJsonAsync(
        JsonStorageOptions options = default, CancellationToken cancel = default)
    {
        var fullData = !options.Seeding;

        var path = options.Path
            ?? Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

        await using var writer = File.Create(path);

        var data = await CardData.CreateAsync(_dbContext, _userManager, fullData, cancel);

        var serialOptions = new JsonSerializerOptions { WriteIndented = true };

        await JsonSerializer.SerializeAsync(writer, data, serialOptions, cancel);
    }


    public async Task<bool> AddFromJsonAsync(
        JsonStorageOptions options = default, CancellationToken cancel = default)
    {
        var path = options.Path 
            ?? Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

        try
        {
            await using var reader = File.OpenRead(path);

            var serialOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var data = await JsonSerializer.DeserializeAsync<CardData>(reader, serialOptions, cancel);

            if (data is null)
            {
                return false;
            }

            if (options.Seeding)
            {
                Seed(data);
            }
            else
            {
                await MergeAsync(data, cancel);
            }

            await _dbContext.SaveChangesAsync(cancel);

            if (!options.Seeding)
            {
                return true;
            }

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


    private async Task MergeAsync(CardData data, CancellationToken cancel)
    {
        var cards = await NewCardsAsync(data, cancel);

        var bins = BinWithBoxAmounts(data).ToList();
        var unclaimed = MergedUnclaimed(data).ToList();

        _dbContext.Cards.AddRange(cards);
        _dbContext.Bins.AddRange(bins);
        _dbContext.Unclaimed.AddRange(unclaimed);
    }


    private async Task<IReadOnlyList<Card>> NewCardsAsync(CardData data, CancellationToken cancel)
    {
        var cardIds = data.Cards
            .Select(c => c.Id)
            .ToArray();

        var dbCardIds = await _dbContext.Cards
            .Select(c => c.Id)
            .Where(cid => cardIds.Contains(cid))
            .ToListAsync(cancel);

        return data.Cards
            .ExceptBy(dbCardIds, c => c.Id)
            .ToList();
    }


    private IEnumerable<Bin> BinWithBoxAmounts(CardData data)
    {
        var boxTable = data.Boxes
            .ToLookup(b => b.BinId);

        var amountTable = data.Amounts
            .ToLookup(ca => ca.LocationId);

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
                box.Cards.AddRange(amounts);
            }

            bin.Id = default;
            bin.Boxes.AddRange(boxes);

            yield return bin;
        }
    }


    private IEnumerable<Unclaimed> MergedUnclaimed(CardData data)
    {
        foreach (var unclaimed in data.Unclaimed)
        {
            yield return unclaimed;
        }

        var amountTable = data.Amounts
            .ToLookup(ca => ca.LocationId);

        var wantTable = data.Wants
            .ToLookup(w => w.LocationId);

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

            unclaimed.Cards.AddRange(unclaimedCards);
            unclaimed.Wants.AddRange(unclaimedWants);

            yield return unclaimed;
        }
    }
}


// TODO: make reading use less memory
internal class CardData
{
    public IReadOnlyList<CardUser> Users { get; set; }
    public IReadOnlyList<UserRef> Refs { get; set; }

    public IReadOnlyList<Card> Cards { get; set; }

    public IReadOnlyList<Deck> Decks { get; set; }
    public IReadOnlyList<Unclaimed> Unclaimed { get; set; }

    public IReadOnlyList<Box> Boxes { get; set; }
    public IReadOnlyList<Bin> Bins { get; set; }

    public IReadOnlyList<Amount> Amounts { get; set; }
    public IReadOnlyList<Want> Wants { get; set; }
    public IReadOnlyList<GiveBack> GiveBacks { get; set; }

    public IReadOnlyList<Change> Changes { get; set; }
    public IReadOnlyList<Transaction> Transactions { get; set; }

    public IReadOnlyList<Trade> Trades { get; set; }
    public IReadOnlyList<Suggestion> Suggestions { get; set; }


    public static async Task<CardData> CreateAsync(
        CardDbContext dbContext,
        UserManager<CardUser> userManager,
        bool fullData,
        CancellationToken cancel = default)
    {
        CardUser[] users;
        UserRef[] refs;

        GiveBack[] giveBacks;

        Change[] changes;
        Transaction[] transactions;

        Trade[] trades;
        Suggestion[] suggestions;

        if (fullData)
        {
            users = await userManager.Users
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancel);

            refs = await dbContext.Users
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancel);

            giveBacks = await dbContext.GiveBacks
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancel);

            changes = await dbContext.Changes
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancel);

            transactions = await dbContext.Transactions
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancel);

            trades = await dbContext.Trades
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancel);

            suggestions = await dbContext.Suggestions
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancel);
        }
        else
        {
            users = Array.Empty<CardUser>();
            refs = Array.Empty<UserRef>();

            giveBacks = Array.Empty<GiveBack>();

            changes = Array.Empty<Change>();
            transactions = Array.Empty<Transaction>();

            trades = Array.Empty<Trade>();
            suggestions = Array.Empty<Suggestion>();
        }

        return new CardData
        {
            Users = users,
            Refs = refs,

            Cards = await dbContext.Cards
                .Include(c => c.Colors)
                .Include(c => c.Types)
                .Include(c => c.Subtypes)
                .Include(c => c.Supertypes)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync(cancel),

            Amounts = await dbContext.Amounts
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync(cancel),

            Decks = await dbContext.Decks
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync(cancel),

            Unclaimed = await dbContext.Unclaimed
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync(cancel),

            Boxes = await dbContext.Boxes
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync(cancel),
            
            Bins = await dbContext.Bins
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync(cancel),

            Wants = await dbContext.Wants
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync(cancel),

            GiveBacks = giveBacks,

            Changes = changes,
            Transactions = transactions,

            Trades = trades,
            Suggestions = suggestions
        };
    }
}