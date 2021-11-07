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

namespace MTGViewer.Services
{
    public readonly struct JsonWriteOptions
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


        public async Task WriteToJsonAsync(string path = null, CancellationToken cancel = default)
        {
            path ??= Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

            await using var writer = File.Create(path);

            var data = await CardData.CreateAsync(_dbContext, _userManager, cancel);

            await JsonSerializer.SerializeAsync(
                writer,
                data,
                new JsonSerializerOptions { WriteIndented = true },
                cancel);
        }


        public async Task<bool> AddFromJsonAsync(
            JsonWriteOptions options = default, CancellationToken cancel = default)
        {
            var path = options.Path 
                ?? Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

            try
            {
                await using var reader = File.OpenRead(path);

                var data = await JsonSerializer.DeserializeAsync<CardData>(
                    reader,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, 
                    cancel);

                // var data = JsonConvert.DeserializeObject<CardData>(
                //     dataStr,
                //     new JsonSerializerSettings
                //     {
                //         MissingMemberHandling = MissingMemberHandling.Error
                //     });

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
                    await Merge(data);
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
            _dbContext.Unclaimed.AddRange(data.Unclaimed ?? Enumerable.Empty<Unclaimed>());

            _dbContext.Amounts.AddRange(data.Amounts);

            _dbContext.Wants.AddRange(data.Wants);
            _dbContext.GiveBacks.AddRange(data.GiveBacks);

            _dbContext.Changes.AddRange(data.Changes);
            _dbContext.Transactions.AddRange(data.Transactions);

            _dbContext.Trades.AddRange(data.Trades);
            _dbContext.Suggestions.AddRange(data.Suggestions);
        }


        private async Task Merge(CardData data)
        {
            var cards = await NewCardsAsync(data);
            var unclaimed = UnclaimedMerged(data);

            _dbContext.Cards.AddRange(cards);

            _dbContext.Bins.AddRange(data.Bins);
            _dbContext.Boxes.AddRange(data.Boxes);

            _dbContext.Unclaimed.AddRange(unclaimed);
        }


        private async Task<IEnumerable<Card>> NewCardsAsync(CardData data)
        {
            var cardIds = data.Cards
                .Select(c => c.Id)
                .ToArray();

            var dbCardIds = await _dbContext.Cards
                .Select(c => c.Id)
                .Where(cid => cardIds.Contains(cid))
                .ToListAsync();

            return data.Cards
                .GroupJoin(dbCardIds,
                    c => c.Id,
                    cid => cid,
                    (card, ids) => (card, any: ids.Any()) )
                .Where(ca => !ca.any)
                .Select(ca => ca.card)
                .ToList();
        }


        private IEnumerable<Unclaimed> UnclaimedMerged(CardData data)
        {
            var unclaimedData = data.Unclaimed ?? Enumerable.Empty<Unclaimed>();

            foreach (var unclaimed in unclaimedData)
            {
                yield return unclaimed;
            }

            var deckAmounts = data.Decks
                .GroupJoin(data.Amounts,
                    d => d.Id,
                    ca => ca.LocationId,
                    (deck, amounts) => (deck, amounts));

            foreach (var (deck, amounts) in deckAmounts)
            {
                var unclaimed = (Unclaimed) deck;

                var unclaimedCards = amounts
                    .Select(ca => new CardAmount
                    {
                        CardId = ca.CardId,
                        Location = unclaimed,
                        Amount = ca.Amount
                    });

                unclaimed.Cards.AddRange(unclaimedCards);

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