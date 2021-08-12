using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Data.Json;


namespace MTGViewer.Tests.Utils
{
    public static class SeedData
    {
        private static readonly Random _random = new Random(100);


        internal static async Task SeedAsync(this CardDbContext dbContext)
        {
            var jsonSuccess = await dbContext.AddFromJsonAsync();

            if (!jsonSuccess)
            {
                await dbContext.AddGeneratedAsync();
                await dbContext.WriteToJsonAsync();
            }

            dbContext.ChangeTracker.Clear();
        }


        private static async Task AddGeneratedAsync(this CardDbContext dbContext)
        {
            var users = GetUsers();
            var cards = await GetCardsAsync();

            var decks = users
                .Where((_, i) => i % 2 == 0)
                .SelectMany(u => Enumerable
                    .Range(0, _random.Next(4))
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
                    Amount = _random.Next(6)
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
                    Amount = _random.Next(5)
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

            await dbContext.AddDataAsync(genData);
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


        private static async Task<IReadOnlyList<Card>> GetCardsAsync()
        {
            return await TestHelpers.NoCacheFetchService()
                .Where(c => c.Cmc, 3)
                .SearchAsync();
        }


        private static IEnumerable<Location> GetSharedLocations()
        {
            yield return new Location("Test Shared");
        }


        internal static async Task<(CardUser Proposer, CardUser Receiver, Location Deck)> GenerateTradeAsync(
            this CardDbContext dbContext)
        {
            var partipants = await dbContext.Users
                .Take(2)
                .ToListAsync();

            var proposer = partipants.First();
            var receiver = partipants.Last();

            var (toLocs, fromLoc, amountTrades) = await dbContext.GetTradeInfoAsync(proposer, receiver);

            var trades = Enumerable
                .Range(0, amountTrades)
                .Zip(fromLoc.Cards, (_, ca) => ca)
                .Select(ca => new Trade
                {
                    Card = ca.Card,
                    Proposer = proposer,
                    Receiver = receiver,
                    To = toLocs[_random.Next(toLocs.Count)],
                    From = ca,
                    Amount = _random.Next(1, ca.Amount)
                });

            dbContext.Trades.AttachRange(trades);
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            return (proposer, receiver, fromLoc);
        }


        private static async Task<(IReadOnlyList<Location> To, Location From, int Amount)> GetTradeInfoAsync(
            this CardDbContext dbContext,
            CardUser proposer, 
            CardUser receiver)
        {
            var cards = await dbContext.Cards.ToListAsync();
            var amountTrades = _random.Next(1, cards.Count / 2);

            var (toLocs, fromLoc) = await dbContext.GetOrCreateLocationsAsync(proposer, receiver);

            var fromCards = fromLoc.Cards.Select(ca => ca.Card);
            var newCards = cards.Except(fromCards);

            while (fromLoc.Cards.Count < amountTrades && newCards.Any())
            {
                var newAmount = new CardAmount
                {
                    Card = newCards.First(),
                    Location = fromLoc,
                    Amount = _random.Next(1, 3)
                };

                dbContext.Amounts.Attach(newAmount);
            }

            var toAmountPair = (Card: fromCards.First(), Location: toLocs.First());

            var toAmount = await dbContext.Amounts
                .SingleOrDefaultAsync(ca => 
                    ca.CardId == toAmountPair.Card.Id
                        && ca.LocationId == toAmountPair.Location.Id
                        && ca.IsRequest == false);

            if (toAmount == default)
            {
                toAmount = new CardAmount
                {
                    Card = toAmountPair.Card,
                    Location = toAmountPair.Location,
                    Amount = 1
                };

                dbContext.Attach(toAmount);
            }

            return (toLocs, fromLoc, amountTrades);
        }


        private static async Task<(IReadOnlyList<Location> To, Location From)> GetOrCreateLocationsAsync(
            this CardDbContext dbContext,
            CardUser proposer, 
            CardUser receiver)
        {
            var toLocs = await dbContext.Locations
                .Where(l => l.OwnerId == proposer.Id)
                .ToListAsync();

            if (!toLocs.Any())
            {
                var toLoc = new Location("Trade deck")
                {
                    Owner = proposer
                };

                dbContext.Attach(toLoc);
                toLocs.Add(toLoc);
            }

            var fromLoc = await dbContext.Locations
                .Include(l => l.Cards)
                    .ThenInclude(ca => ca.Card)
                .FirstOrDefaultAsync(l => l.OwnerId == receiver.Id);

            if (fromLoc == default)
            {
                fromLoc = new Location("Trade deck")
                {
                    Owner = receiver
                };

                dbContext.Attach(fromLoc);
            }

            return (toLocs, fromLoc);
        }
    }
}