using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
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


        internal static async Task SeedAsync(
            this CardDbContext dbContext, UserManager<CardUser> userManager = null)
        {
            await _jsonLock.WaitAsync();
            try
            {
                var jsonSuccess = await dbContext.AddFromJsonAsync(userManager);

                if (!jsonSuccess)
                {
                    userManager ??= TestFactory.CardUserManager();
                    await dbContext.AddGeneratedAsync(userManager);
                    await dbContext.WriteToJsonAsync(userManager);
                }

                dbContext.ChangeTracker.Clear();
            }
            finally
            {
                _jsonLock.Release();
            }
        }


        private static async Task AddGeneratedAsync(
            this CardDbContext dbContext, UserManager<CardUser> userManager)
        {
            var users = GetUsers();
            var userRefs = users.Select(u => new UserRef(u)).ToList();

            await Task.WhenAll(users.Select(userManager.CreateAsync));
            dbContext.Users.AddRange(userRefs);

            var cards = await GetCardsAsync();
            var decks = GetDecks(userRefs);
            var locations = GetShares().Concat(decks).ToList();

            dbContext.Cards.AddRange(cards);
            dbContext.Locations.AddRange(locations);

            var amounts = GetCardAmounts(cards, locations);

            dbContext.Amounts.AddRange(amounts);

            var transfers = GetTransfers(userRefs, cards, decks, amounts);

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
            return await TestFactory.NoCacheFetchService()
                .Where(c => c.Cmc, 3)
                .SearchAsync();
        }


        private static IReadOnlyList<Location> GetShares()
        {
            return Enumerable.Range(0, 3)
                .Select(i => new Shared($"Box #{i+1}"))
                .ToList();
        }


        private static IReadOnlyList<Deck> GetDecks(IEnumerable<UserRef> users)
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
            IEnumerable<UserRef> users,
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


        internal static async Task<Deck> CreateDeckAsync(this CardDbContext dbContext)
        {
            var users = await dbContext.Users.ToListAsync();
            var owner = users[_random.Next(users.Count)];

            var cards = await dbContext.Cards.ToListAsync();
            var numCards = _random.Next(1, cards.Count / 2);

            var deckCards = cards
                .Select(card => (card, key: _random.Next(cards.Count)))
                .OrderBy(ck => ck.key)
                .Take(numCards)
                .Select(ck => ck.card)
                .ToList();

            var newDeck = new Deck("Test Deck")
            {
                Owner = owner
            };

            var shared = await dbContext.Shares.FirstAsync();
            var sharedAmounts = deckCards
                .Select(c => new CardAmount
                {
                    Card = c,
                    Location = shared,
                    Amount = 1
                });

            var deckAmounts = deckCards
                .Select(c => new CardAmount
                {
                    Card = c,
                    Location = newDeck,
                    Amount = _random.Next(1, 3)
                });

            var amounts = sharedAmounts
                .Concat(deckAmounts)
                .ToList();

            dbContext.Attach(newDeck);
            dbContext.AttachRange(amounts);

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            return newDeck;
        }


        internal static async Task<Deck> CreateRequestDeckAsync(this CardDbContext dbContext)
        {
            var users = await dbContext.Users.ToListAsync();
            var owner = users[_random.Next(users.Count)];

            var cardOptions = await dbContext.Decks
                .Where(d => d.OwnerId != owner.Id)
                .SelectMany(ca => ca.Cards)
                .Select(ca => ca.Card)
                .Distinct()
                .ToListAsync();

            if (cardOptions.Count < 2)
            {
                var optionIds = cardOptions.Select(c => c.Id).ToArray();
                var nonOwner = users.First(u => u.Id != owner.Id);
                var card = await dbContext.Cards
                    .FirstAsync(c => !optionIds.Contains(c.Id));

                var decks = new List<Deck>()
                {
                    new Deck("Source #1"),
                    new Deck("Source #2")
                };

                var amounts = decks
                    .Select(deck => new CardAmount
                    {
                        Card = card,
                        Location = deck
                    });

                dbContext.Amounts.AddRange(amounts);
                cardOptions.Add(card);
            }

            var numRequests = _random.Next(1, cardOptions.Count / 2);

            var targetCards = cardOptions
                .Select(card => (card, key: _random.Next(cardOptions.Count)))
                .OrderBy(ck => ck.key)
                .Take(numRequests)
                .Select(ck => ck.card);

            var newDeck = new Deck("Request Deck")
            {
                Owner = owner
            };

            var requests = targetCards
                .Select(card => new CardAmount
                {
                    Card = card,
                    Location = newDeck,
                    IsRequest = true
                })
                .ToList();

            dbContext.Decks.Attach(newDeck);
            dbContext.Amounts.AttachRange(requests);

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            return newDeck;
        }



        internal static async Task<TradeSet> CreateTradeSetAsync(this CardDbContext dbContext)
        {
            var users = await dbContext.Users
                .ToListAsync();
            
            var partipants = users
                .Select(user => (user, key: _random.Next(users.Count)))
                .OrderBy(uk => uk.key)
                .Take(2)
                .Select(uk => uk.user)
                .ToList();

            var proposer = partipants[0];
            var receiver = partipants[1];

            var trades = await dbContext.CreateTradesAsync(proposer, receiver);

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            return new TradeSet(trades);
        }


        private record TradeLocations(Deck To, IReadOnlyList<Deck> From) { }


        private static async Task<IReadOnlyList<Trade>> CreateTradesAsync(
            this CardDbContext dbContext,
            UserRef proposer, 
            UserRef receiver)
        {
            var (to, froms) = await dbContext.GetLocationsAsync(proposer, receiver);

            var cards = await dbContext.Cards.ToListAsync();
            var amountTrades = _random.Next(1, cards.Count / 2);

            var trades = new List<Trade>();

            foreach(var tradeCard in cards.Take(amountTrades))
            {
                var from = froms[_random.Next(froms.Count)];

                var fromAmount = await dbContext.GetFromAmountAsync(tradeCard, from);
                var toRequest = await dbContext.GetToRequestAsync(
                    tradeCard, to, fromAmount.Amount);

                trades.Add(new Trade
                {
                    Card = tradeCard,
                    Proposer = proposer,
                    Receiver = receiver,
                    To = to,
                    From = from,
                    Amount = toRequest.Amount
                });
            }

            dbContext.Trades.AddRange(trades);

            return trades;
        }


        private static async Task<TradeLocations> GetLocationsAsync(
            this CardDbContext dbContext,
            UserRef proposer, 
            UserRef receiver)
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


        private static async Task<CardAmount> GetFromAmountAsync(
            this CardDbContext dbContext, Card card, Deck from)
        {
            var fromAmount = await dbContext.Amounts
                .SingleOrDefaultAsync(ca => !ca.IsRequest
                    && ca.CardId == card.Id
                    && ca.LocationId == from.Id);

            if (fromAmount == default)
            {
                fromAmount = new CardAmount
                {
                    Card = card,
                    Location = from,
                    Amount = _random.Next(1, 3)
                };

                dbContext.Attach(fromAmount);
            }

            return fromAmount;
        }


        private static async Task<CardAmount> GetToRequestAsync(
            this CardDbContext dbContext, Card card, Deck to, int maxAmount)
        {
            var toRequest = await dbContext.Amounts
                .SingleOrDefaultAsync(ca => ca.IsRequest
                    && ca.CardId == card.Id
                    && ca.LocationId == to.Id);

            if (toRequest == default)
            {
                toRequest = new CardAmount
                {
                    Card = card,
                    Location = to,
                    Amount = _random.Next(1, maxAmount),
                    IsRequest = true
                };

                dbContext.Amounts.Attach(toRequest);
            }

            return toRequest;
        }
    }
}