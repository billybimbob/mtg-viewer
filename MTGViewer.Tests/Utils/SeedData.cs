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

            dbContext.Users.AddRange(users);
            dbContext.Cards.AddRange(cards);

            var decks = users
                .Where((_, i) => i % 2 == 0)
                .SelectMany(u => Enumerable
                    .Range(0, _random.Next(4))
                    .Select(i => new Deck($"Deck #{i+1}")
                    {
                        Owner = u
                    }));
                    
            var locations = GetSharedLocations()
                .Concat(decks)
                .ToList();

            dbContext.Locations.AddRange(locations);

            var amounts = cards
                .Zip(locations, (card, location) => (card, location))
                .Select(cl => new CardAmount
                {
                    Card = cl.card,
                    Location = cl.location,
                    Amount = _random.Next(6)
                })
                .ToList();

            dbContext.Amounts.AddRange(amounts);

            var source = amounts.First(ca => ca.Location.Type != Discriminator.Shared);

            var tradeFrom = (Deck)source.Location;
            var tradeTo = decks.First(l => l.Id != source.LocationId);

            var suggestCard = cards.First();
            var suggester = users.First(u => 
                u.Id != tradeFrom.OwnerId && u.Id != tradeTo.OwnerId);

            var transfers = new List<Transfer>()
            {
                new Trade
                {
                    Card = source.Card,
                    Proposer = tradeTo.Owner,
                    Receiver = tradeFrom.Owner,
                    To = tradeTo,
                    From = tradeFrom,
                    Amount = _random.Next(5)
                },
                new Suggestion
                {
                    Card = suggestCard,
                    Proposer = suggester,
                    Receiver = tradeTo.Owner,
                    To = tradeTo
                }
            };

            dbContext.Transfers.AddRange(transfers);

            await dbContext.SaveChangesAsync();
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
            yield return new Shared("Test Shared");
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
                    From = ca.Location as Deck,
                    Amount = _random.Next(1, ca.Amount)
                });

            dbContext.Trades.AttachRange(trades);
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            return (proposer, receiver, fromLoc);
        }


        private static async Task<(IReadOnlyList<Deck> To, Deck From, int Amount)> GetTradeInfoAsync(
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

            var toAmountPair = (Card: fromCards.First(), Deck: toLocs.First());

            var toAmount = await dbContext.Amounts
                .SingleOrDefaultAsync(ca => 
                    ca.CardId == toAmountPair.Card.Id
                        && ca.LocationId == toAmountPair.Deck.Id
                        && ca.IsRequest == false);

            if (toAmount == default)
            {
                toAmount = new CardAmount
                {
                    Card = toAmountPair.Card,
                    Location = toAmountPair.Deck,
                    Amount = 1
                };

                dbContext.Attach(toAmount);
            }

            return (toLocs, fromLoc, amountTrades);
        }


        private static async Task<(IReadOnlyList<Deck> To, Deck From)> GetOrCreateLocationsAsync(
            this CardDbContext dbContext,
            CardUser proposer, 
            CardUser receiver)
        {
            var toLocs = await dbContext.Decks
                .Where(l => l.OwnerId == proposer.Id)
                .ToListAsync();

            if (!toLocs.Any())
            {
                var toLoc = new Deck("Trade deck")
                {
                    Owner = proposer
                };

                dbContext.Attach(toLoc);
                toLocs.Add(toLoc);
            }

            var fromLoc = await dbContext.Decks
                .Include(l => l.Cards)
                    .ThenInclude(ca => ca.Card)
                .FirstOrDefaultAsync(l => l.OwnerId == receiver.Id);

            if (fromLoc == default)
            {
                fromLoc = new Deck("Trade deck")
                {
                    Owner = receiver
                };

                dbContext.Attach(fromLoc);
            }

            return (toLocs, fromLoc);
        }
    }
}