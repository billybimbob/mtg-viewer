using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Data.Json;


namespace MTGViewer.Tests.Utils
{
    public static class SeedData
    {
        private static readonly Random _random = new(100);
        private static readonly SemaphoreSlim _jsonLock = new(1, 1);


        internal static async Task SeedAsync(this CardDbContext dbContext)
        {
            await _jsonLock.WaitAsync();
            try
            {
                var jsonSuccess = await dbContext.AddFromJsonAsync();

                if (!jsonSuccess)
                {
                    await dbContext.AddGeneratedAsync();
                    await dbContext.WriteToJsonAsync();
                }

                dbContext.ChangeTracker.Clear();
            }
            finally
            {
                _jsonLock.Release();
            }
        }


        private static async Task AddGeneratedAsync(this CardDbContext dbContext)
        {
            var users = GetUsers();

            dbContext.Users.AddRange(users);

            var cards = await GetCardsAsync();
            var decks = GetDecks(users);
            var locations = GetShares().Concat(decks).ToList();

            dbContext.Cards.AddRange(cards);
            dbContext.Locations.AddRange(locations);

            var amounts = GetCardAmounts(cards, locations);

            dbContext.Amounts.AddRange(amounts);

            var transfers = GetTransfers(users, cards, decks, amounts);

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


        private static IEnumerable<Location> GetShares()
        {
            yield return new Shared("Test Shared");
        }


        private static IReadOnlyList<Deck> GetDecks(IEnumerable<CardUser> users)
        {
            return users
                .Where((_, i) => i % 2 == 0)
                .SelectMany(u => Enumerable
                    .Range(0, _random.Next(1, 4))
                    .Select(i => new Deck($"Deck #{i+1}")
                    {
                        Owner = u
                    }))
                .ToList();
        }


        private static IReadOnlyList<CardAmount> GetCardAmounts(
            IEnumerable<Card> cards,
            IEnumerable<Location> locations)
        {
            return cards.Zip(locations,
                (card, location) => (card, location))
                .Select(cl => new CardAmount
                {
                    Card = cl.card,
                    Location = cl.location,
                    Amount = _random.Next(6)
                })
                .ToList();
        }


        private static IReadOnlyList<Transfer> GetTransfers(
            IEnumerable<CardUser> users,
            IEnumerable<Card> cards,
            IEnumerable<Deck> decks,
            IEnumerable<CardAmount> amounts)
        {
            var source = amounts.First(ca => ca.Location is Deck);

            var tradeFrom = (Deck)source.Location;
            var tradeTo = decks.First(l => l.Id != source.LocationId);

            var suggestCard = cards.First();
            var suggester = users.First(u => 
                u.Id != tradeFrom.OwnerId && u.Id != tradeTo.OwnerId);

            return new List<Transfer>()
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
        }


        
        internal record TradeInfo(CardUser Proposer, CardUser Receiver, Deck From) { }

        private record TradeOptions(Deck To, IReadOnlyList<Deck> From, int Amount) { }

        private record TradeLocations(Deck To, IReadOnlyList<Deck> From) { }


        internal static async Task<TradeInfo> GenerateTradeAsync(this CardDbContext dbContext)
        {
            var partipants = await dbContext.Users
                .Take(2)
                .ToListAsync();

            var proposer = partipants.First();
            var receiver = partipants.Last();

            var (toLoc, fromLocs, amountTrades) = await dbContext.GetTradeInfoAsync(proposer, receiver);

            var trades = Enumerable
                .Range(0, amountTrades)
                .Zip(toLoc.Cards, (_, ca) => ca)
                .Select(ca => new Trade
                {
                    Card = ca.Card,
                    Proposer = proposer,
                    Receiver = receiver,
                    To = toLoc,
                    From = fromLocs[_random.Next(fromLocs.Count)],
                    Amount = _random.Next(1, ca.Amount)
                })
                .ToList();

            var requests = trades
                .Select(t => new CardAmount
                {
                    Card = t.Card,
                    Location = toLoc,
                    Amount = t.Amount,
                    IsRequest = true
                })
                .ToList();

            var validFrom = fromLocs.First(d => d.Cards.Any());

            dbContext.Trades.AttachRange(trades);
            dbContext.Amounts.AttachRange(requests);

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            return new TradeInfo(proposer, receiver, validFrom);
        }


        private static async Task<TradeOptions> GetTradeInfoAsync(
            this CardDbContext dbContext,
            CardUser proposer, 
            CardUser receiver)
        {
            var cards = await dbContext.Cards.ToListAsync();
            var amountTrades = _random.Next(1, cards.Count / 2);

            var (toLoc, fromLocs) = await dbContext.GetOrCreateLocationsAsync(proposer, receiver);

            var toCards = toLoc.Cards.Select(ca => ca.Card);
            var newCards = cards.Except(toCards);

            while (toLoc.Cards.Count < amountTrades && newCards.Any())
            {
                var newAmount = new CardAmount
                {
                    Card = newCards.First(),
                    Location = toLoc,
                    Amount = _random.Next(1, 3)
                };

                dbContext.Amounts.Attach(newAmount);
            }

            var fromAmountPair = (Card: toCards.First(), Deck: fromLocs.First());

            var fromAmount = await dbContext.Amounts
                .SingleOrDefaultAsync(ca => 
                    ca.CardId == fromAmountPair.Card.Id
                        && ca.LocationId == fromAmountPair.Deck.Id
                        && ca.IsRequest == false);

            if (fromAmount == default)
            {
                fromAmount = new CardAmount
                {
                    Card = fromAmountPair.Card,
                    Location = fromAmountPair.Deck,
                    Amount = 1
                };

                dbContext.Attach(fromAmount);
            }

            return new TradeOptions(toLoc, fromLocs, amountTrades);
        }


        private static async Task<TradeLocations> GetOrCreateLocationsAsync(
            this CardDbContext dbContext,
            CardUser proposer, 
            CardUser receiver)
        {
            var toLoc = await dbContext.Decks
                .Include(l => l.Cards)
                    .ThenInclude(ca => ca.Card)
                .FirstOrDefaultAsync(l => l.OwnerId == proposer.Id);

            if (toLoc == default)
            {
                toLoc = new Deck("Trade deck")
                {
                    Owner = proposer
                };

                dbContext.Attach(toLoc);
            }

            var fromLocs = await dbContext.Decks
                .Where(l => l.OwnerId == receiver.Id)
                .ToListAsync();

            if (!fromLocs.Any())
            {
                var fromLoc = new Deck("Trade deck")
                {
                    Owner = receiver
                };

                dbContext.Attach(fromLoc);
                fromLocs.Add(fromLoc);
            }

            return new TradeLocations(toLoc, fromLocs);
        }
    }
}