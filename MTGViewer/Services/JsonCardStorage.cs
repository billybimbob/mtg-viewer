using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using Newtonsoft.Json;
using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Services
{
    public readonly struct JsonWriteOptions
    {
        public string Path { get; init; }

        public bool IncludeUsers { get; init; }
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

            var seedOptions = new SeedOptions();
            config.GetSection(SeedOptions.Seed).Bind(seedOptions);

            _tempPassword = seedOptions.Password;
        }


        public async Task WriteToJsonAsync(string path = null, CancellationToken cancel = default)
        {
            path ??= Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

            await using var writer = File.CreateText(path);

            var data = await CardData.CreateAsync(_dbContext, _userManager, cancel);
            var dataStr = JsonConvert.SerializeObject(data, Formatting.Indented);

            await writer.WriteAsync(dataStr);
        }


        public async Task<bool> AddFromJsonAsync(
            JsonWriteOptions options = default, CancellationToken cancel = default)
        {
            var path = options.Path 
                ?? Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

            try
            {
                using var reader = File.OpenText(path);

                var dataStr = await reader.ReadToEndAsync();
                var data = JsonConvert.DeserializeObject<CardData>(
                    dataStr,
                    new JsonSerializerSettings
                    {
                        MissingMemberHandling = MissingMemberHandling.Error
                    });

                if (data is null)
                {
                    return false;
                }

                _dbContext.Users.AddRange(data.Refs);
                _dbContext.Cards.AddRange(data.Cards);

                _dbContext.Bins.AddRange(data.Bins);
                _dbContext.Boxes.AddRange(data.Boxes);
                _dbContext.Decks.AddRange(data.Decks);

                _dbContext.Amounts.AddRange(data.Amounts);

                _dbContext.Wants.AddRange(data.Wants);
                _dbContext.GiveBacks.AddRange(data.GiveBacks);

                _dbContext.Changes.AddRange(data.Changes);
                _dbContext.Transactions.AddRange(data.Transactions);

                _dbContext.Trades.AddRange(data.Trades);
                _dbContext.Suggestions.AddRange(data.Suggestions);

                await _dbContext.SaveChangesAsync(cancel);

                if (!options.IncludeUsers)
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
            catch (JsonSerializationException)
            {
                return false;
            }
            catch (DbUpdateException)
            {
                return false;
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
        public IReadOnlyList<Box> Boxes { get; set; }
        public IReadOnlyList<Bin> Bins { get; set; }

        public IReadOnlyList<CardAmount> Amounts { get; set; }

        public IReadOnlyList<Want> Wants { get; set; }
        public IReadOnlyList<GiveBack> GiveBacks { get; set; }

        public IReadOnlyList<Change> Changes { get; set; }
        public IReadOnlyList<Transaction> Transactions { get; set; }

        public IReadOnlyList<Trade> Trades { get; set; }
        public IReadOnlyList<Suggestion> Suggestions { get; set; }


        public static async Task<CardData> CreateAsync(
            CardDbContext dbContext,
            UserManager<CardUser> userManager = default,
            CancellationToken cancel = default)
        {
            return new CardData
            {
                Users = await userManager.Users
                    .AsNoTrackingWithIdentityResolution()
                    .ToListAsync(cancel),

                Refs = await dbContext.Users
                    .AsNoTrackingWithIdentityResolution()
                    .ToListAsync(cancel),

                Cards = await dbContext.Cards
                    .Include(c => c.Colors)
                    .Include(c => c.Types)
                    .Include(c => c.SubTypes)
                    .Include(c => c.SuperTypes)
                    .AsSplitQuery()
                    .AsNoTrackingWithIdentityResolution()
                    .ToListAsync(cancel),

                Amounts = await dbContext.Amounts
                    .AsNoTrackingWithIdentityResolution()
                    .ToListAsync(cancel),

                Decks = await dbContext.Decks
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

                GiveBacks = await dbContext.GiveBacks
                    .AsNoTrackingWithIdentityResolution()
                    .ToListAsync(cancel),

                Changes = await dbContext.Changes
                    .AsNoTrackingWithIdentityResolution()
                    .ToListAsync(cancel),

                Transactions = await dbContext.Transactions
                    .AsNoTrackingWithIdentityResolution()
                    .ToListAsync(cancel),

                Trades = await dbContext.Trades
                    .AsNoTrackingWithIdentityResolution()
                    .ToListAsync(cancel),

                Suggestions = await dbContext.Suggestions
                    .AsNoTrackingWithIdentityResolution()
                    .ToListAsync(cancel)
            };
        }
    }
}