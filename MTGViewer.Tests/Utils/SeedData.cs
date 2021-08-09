using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Tests.Utils
{
    public static class SeedData
    {
        private static readonly Random random = new Random(100);

        internal static async Task AddTo(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            var users = GetUsers();
            var cards = await GetCards();
            var locations = GetLocations().ToList();

            var newLocs = users
                .Where((_, i) => i % 2 == 0)
                .SelectMany(u => Enumerable
                    .Range(0, random.Next(2))
                    .Select(i => new Location($"Deck #{i}")
                    {
                        Owner = u
                    }));

            locations.AddRange(newLocs);

            var amounts = cards
                .Where((_, i) => i % 2 == 0)
                .Zip(locations)
                .Select(cl => new CardAmount
                {
                    Card = cl.First,
                    Location = cl.Second,
                    Amount = random.Next(6)
                });

            await Task.WhenAll( users.Select(userManager.CreateAsync) );

            dbContext.Cards.AddRange(cards);
            dbContext.Locations.AddRange(locations);
            dbContext.Amounts.AddRange(amounts);

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