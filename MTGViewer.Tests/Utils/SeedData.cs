using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Data.Json;


namespace MTGViewer.Tests.Utils
{
    public static class SeedData
    {
        private static readonly Random random = new Random(100);


        public static async Task Seed(this CardDbContext dbContext)
        {
            var jsonSuccess = await dbContext.AddFromJson();

            if (!jsonSuccess)
            {
                await dbContext.AddGenerated();
                await dbContext.WriteToJson();
            }

            dbContext.ChangeTracker.Clear();
        }


        private static async Task AddGenerated(this CardDbContext dbContext)
        {
            var users = GetUsers();
            var cards = await GetCards();

            var decks = users
                .Where((_, i) => i % 2 == 0)
                .SelectMany(u => Enumerable
                    .Range(0, random.Next(4))
                    .Select(i => new Location($"Deck #{i+1}")
                    {
                        Owner = u
                    }));
                    
            var locations = GetSharedLocations()
                .Concat(decks)
                .ToList();

            var amounts = cards
                .Zip(locations, (card, location) => (card, location))
                .Select(cl => new CardAmount
                {
                    Card = cl.card,
                    Location = cl.location,
                    Amount = random.Next(6)
                })
                .ToList();

            var tradeFrom = amounts.First(ca => !ca.Location.IsShared);
            var tradeTo = locations.First(l => 
                !l.IsShared && l.Id != tradeFrom.LocationId);

            var suggestCard = cards.First();
            var suggester = users.First(u => 
                u.Id != tradeFrom.Location.OwnerId
                    && u.Id != tradeTo.OwnerId);

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

            var genData = new CardData
            {
                Users = users,
                Cards = cards,
                Locations = locations,
                Amounts = amounts,
                Trades = trades
            };

            await dbContext.AddData(genData);
        }


        private static IReadOnlyList<CardUser> GetUsers() => new List<CardUser>()
        {
            new CardUser
            {
                Name = "Test Name",
                UserName = "testingname",
                Email = "test@gmail.com"
            },
            new CardUser
            {
                Name = "Bob Billy",
                UserName = "bobbilly213",
                Email = "bob@gmail.com"
            },
            new CardUser
            {
                Name = "Steve Phil",
                UserName = "stephenthegreat",
                Email = "steve@gmail.com"
            }
        };


        private static async Task<IReadOnlyList<Card>> GetCards()
        {
            return await TestHelpers.NoCacheFetchService()
                .Where(c => c.Cmc, 3)
                .SearchAsync();
        }


        private static IEnumerable<Location> GetSharedLocations()
        {
            yield return new Location("Test Shared");
        }
    }
}