using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;
using MTGViewer.Areas.Identity.Data;


namespace MTGViewer.Data.Seed
{
    // TODO: make reading use less memory

    public class CardData
    {
        public IReadOnlyList<CardUser> Users { get; set; }
        public IReadOnlyList<UserRef> Refs { get; set; }

        public IReadOnlyList<Card> Cards { get; set; }

        public IReadOnlyList<Deck> Decks { get; set; }
        public IReadOnlyList<Box> Boxes { get; set; }
        public IReadOnlyList<Bin> Bins { get; set; }

        public IReadOnlyList<CardAmount> Amounts { get; set; }
        public IReadOnlyList<CardRequest> Requests { get; set; }

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
                Users = userManager is null
                    ? new List<CardUser>()
                    : await userManager.Users
                        .AsNoTrackingWithIdentityResolution()
                        .ToListAsync(cancel),

                Refs = await dbContext.Users
                    .AsNoTrackingWithIdentityResolution()
                    .ToListAsync(),

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

                Requests = await dbContext.Requests
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


    public static class Storage
    {
        public const string USER_PASSWORD = "Password1!";
        private const string CARDS_JSON = "cards.json";

        public static async Task WriteToJsonAsync(
            CardDbContext dbContext,
            UserManager<CardUser> userManager,
            string path = default,
            CancellationToken cancel = default)
        {
            var data = await CardData.CreateAsync(dbContext, userManager, cancel);

            path ??= Path.Combine(Directory.GetCurrentDirectory(), CARDS_JSON);

            await using var writer = File.CreateText(path);

            var dataStr = JsonConvert.SerializeObject(data, Formatting.Indented);
            await writer.WriteAsync(dataStr);
        }


        public static async Task<bool> AddFromJsonAsync(
            CardDbContext dbContext,
            UserManager<CardUser> userManager = default,
            string path = default,
            CancellationToken cancel = default)
        {
            path ??= Path.Combine(Directory.GetCurrentDirectory(), CARDS_JSON);
            var cardsPath = Path.ChangeExtension(path, ".json");

            try
            {
                using var reader = File.OpenText(cardsPath);

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

                dbContext.Users.AddRange(data.Refs);
                dbContext.Cards.AddRange(data.Cards);

                dbContext.Bins.AddRange(data.Bins);
                dbContext.Boxes.AddRange(data.Boxes);
                dbContext.Decks.AddRange(data.Decks);

                dbContext.Amounts.AddRange(data.Amounts);
                dbContext.Requests.AddRange(data.Requests);

                dbContext.Changes.AddRange(data.Changes);
                dbContext.Transactions.AddRange(data.Transactions);

                dbContext.Trades.AddRange(data.Trades);
                dbContext.Suggestions.AddRange(data.Suggestions);

                await dbContext.SaveChangesAsync(cancel);

                if (userManager is not null && (data.Users?.Any() ?? false))
                {
                    await Task.WhenAll(
                        data.Users.Select(u => userManager.CreateAsync(u, USER_PASSWORD)) );
                }

                return true;
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
}