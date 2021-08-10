using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using MTGViewer.Areas.Identity.Data;


namespace MTGViewer.Data.Json
{
    // TODO: make reading use less memory

    public class CardData
    {
        public List<CardUser> Users { get; set; }
        public List<Card> Cards { get; set; }
        public List<CardAmount> Amounts { get; set; }
        public List<Location> Locations { get; set; }
        public List<Trade> Trades { get; set; }

        public static async Task<CardData> Create(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            // TODO: add some includes possibly?
            return new CardData
            {
                Users = await userManager.Users.ToListAsync(),
                Cards = await dbContext.Cards.ToListAsync(),
                Amounts = await dbContext.Amounts.ToListAsync(),
                Locations = await dbContext.Locations.ToListAsync(),
                Trades = await dbContext.Trades.ToListAsync()
            };
        }
    }


    public static class Storage
    {
        private const string CARDS_JSON = "cards.json";

        public static async Task WriteToJson(
            UserManager<CardUser> userManager, CardDbContext dbContext, string directory = null)
        {
            var data = await CardData.Create(userManager, dbContext);

            directory ??= Directory.GetCurrentDirectory();
            var cardsPath = Path.Combine(directory, CARDS_JSON);

            await using var writer = File.CreateText(cardsPath);

            var dataStr = JsonConvert.SerializeObject(data, Formatting.Indented);
            await writer.WriteAsync(dataStr);
        }


        public static async Task<bool> AddFromJson(
            UserManager<CardUser> userManager, CardDbContext dbContext, string directory = null)
        {
            try
            {
                directory ??= Directory.GetCurrentDirectory();
                var cardsPath = Path.Combine(directory, CARDS_JSON);

                using var reader = File.OpenText(cardsPath);

                var dataStr = await reader.ReadToEndAsync();
                var data = JsonConvert.DeserializeObject<CardData>(dataStr);

                if (data is null)
                {
                    return false;
                }

                await Task.WhenAll(data.Users.Select(userManager.CreateAsync));

                dbContext.Cards.AddRange(data.Cards);
                dbContext.Amounts.AddRange(data.Amounts);
                dbContext.Locations.AddRange(data.Locations);
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