using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using MTGViewer.Areas.Identity.Data;


namespace MTGViewer.Data.Json
{
    // TODO: make reading use less memory

    public class CardData
    {
        public IReadOnlyList<CardUser> Users { get; set; }
        public IReadOnlyList<Card> Cards { get; set; }

        public IReadOnlyList<CardAmount> Amounts { get; set; }

        public IReadOnlyList<Shared> Shares { get; set; }
        public IReadOnlyList<Deck> Decks { get; set; }

        public IReadOnlyList<Suggestion> Suggestions { get; set; }
        public IReadOnlyList<Trade> Trades { get; set; }


        public static async Task<CardData> CreateAsync(CardDbContext dbContext)
        {
            // TODO: add some includes possibly?
            return new CardData
            {
                Users = await dbContext.Users
                    .AsNoTracking()
                    .ToListAsync(),

                Cards = await dbContext.Cards
                    .Include(c => c.Colors)
                    .Include(c => c.Types)
                    .Include(c => c.SubTypes)
                    .Include(c => c.SuperTypes)
                    .AsSplitQuery()
                    .AsNoTracking()
                    .ToListAsync(),

                Amounts = await dbContext.Amounts
                    .AsNoTracking()
                    .ToListAsync(),

                Shares = await dbContext.Shares
                    .AsNoTracking()
                    .ToListAsync(),

                Decks = await dbContext.Decks
                    .AsNoTracking()
                    .ToListAsync(),

                Suggestions = await dbContext.Suggestions
                    .AsNoTracking()
                    .ToListAsync(),

                Trades = await dbContext.Trades
                    .AsNoTracking()
                    .ToListAsync()
            };
        }
    }


    public static class Storage
    {
        private const string CARDS_JSON = "cards.json";

        public static async Task WriteToJsonAsync(this CardDbContext dbContext, string directory = null)
        {
            var data = await CardData.CreateAsync(dbContext);

            directory ??= Directory.GetCurrentDirectory();
            var cardsPath = Path.Combine(directory, CARDS_JSON);

            await using var writer = File.CreateText(cardsPath);

            var dataStr = JsonConvert.SerializeObject(data, Formatting.Indented);
            await writer.WriteAsync(dataStr);
        }


        public static async Task<bool> AddFromJsonAsync(this CardDbContext dbContext, string directory = null)
        {
            directory ??= Directory.GetCurrentDirectory();
            var cardsPath = Path.Combine(directory, CARDS_JSON);

            try
            {
                using var reader = File.OpenText(cardsPath);

                var dataStr = await reader.ReadToEndAsync();
                var data = JsonConvert.DeserializeObject<CardData>(dataStr);

                if (data is null)
                {
                    return false;
                }

                dbContext.Users.AddRange(data.Users);
                dbContext.Cards.AddRange(data.Cards);

                dbContext.Amounts.AddRange(data.Amounts);

                dbContext.Shares.AddRange(data.Shares);
                dbContext.Decks.AddRange(data.Decks);

                dbContext.Suggestions.AddRange(data.Suggestions);
                dbContext.Trades.AddRange(data.Trades);

                await dbContext.SaveChangesAsync();

                return true;
            }
            catch (JsonReaderException)
            {
                return false;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }
    }
}