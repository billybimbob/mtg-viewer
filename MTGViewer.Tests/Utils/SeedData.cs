using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Data.Json;


namespace MTGViewer.Tests.Utils
{
    public static class SeedData
    {
        private static readonly Random random = new Random(100);


        internal static async Task AddTo(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            var jsonSuccess = await Storage.AddFromJson(userManager, dbContext);

            if (!jsonSuccess)
            {
                await AddFromSeeds(userManager, dbContext);
                await Storage.WriteToJson(userManager, dbContext);
            }
        }


        private static async Task AddFromSeeds(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            var users = GetUsers();

            await Task.WhenAll( users.Select(userManager.CreateAsync) );

            var cards = await GetCards();
            var locations = GetLocations().ToList();

            var newLocs = users
                .Where((_, i) => i % 2 == 0)
                .SelectMany(u => Enumerable
                    .Range(0, random.Next(4))
                    .Select(i => new Location($"Deck #{i}")
                    {
                        Owner = u
                    }));

            locations.AddRange(newLocs);

            var amounts = cards
                .Zip(locations, (card, location) => (card, location))
                .Select(cl => new CardAmount
                {
                    Card = cl.card,
                    Location = cl.location,
                    Amount = random.Next(6)
                })
                .ToList();

            dbContext.Cards.AddRange(cards);
            dbContext.Locations.AddRange(locations);
            dbContext.Amounts.AddRange(amounts);

            var tradeFrom = amounts.First();
            var tradeTo = locations.First(l => l.Id != tradeFrom.LocationId);

            var suggestCard = cards.First();
            var suggester = users.First(u => 
                u.Id != tradeFrom.Location.OwnerId && u.Id != tradeTo.OwnerId);

            var trades = new List<Trade>()
            {
                new Trade
                {
                    Card = tradeFrom.Card,
                    Proposer = tradeTo.Owner,
                    Receiver = tradeFrom.Location.Owner,
                    To = tradeTo,
                    From = tradeFrom,
                    Amount = random.Next(5)
                },
                new Trade
                {
                    Card = suggestCard,
                    Proposer = suggester,
                    Receiver = tradeTo.Owner,
                    To = tradeTo
                }
            };

            dbContext.Trades.AddRange(trades);

            await dbContext.SaveChangesAsync();
        }


        private static IEnumerable<CardUser> GetUsers()
        {
            yield return new CardUser
            {
                Name = "Test Name",
                UserName = "testingname",
                Email = "test@gmail.com"
            };

            yield return new CardUser
            {
                Name = "Bob Billy",
                UserName = "bobbilly213",
                Email = "bob@gmail.com"
            };

            yield return new CardUser
            {
                Name = "Steve Phil",
                UserName = "stephenthegreat",
                Email = "steve@gmail.com"
            };
        }


        private static async Task<IEnumerable<Card>> GetCards()
        {
            // TODO: do not use fetch for seeding since slow
            var fetch = TestHelpers.NoCacheFetchService();
            return await fetch
                .Where(c => c.Cmc, 3)
                .SearchAsync();
        }


        private static IEnumerable<Location> GetLocations()
        {
            yield return new Location("Test Shared");
        }
    }
}