using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Data.Seed;


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
                // don't use card gen to allow for null userManager
                var jsonSuccess = await Storage.AddFromJsonAsync(dbContext, userManager);

                if (!jsonSuccess)
                {
                    userManager ??= TestFactory.CardUserManager();
                    var cardGen = TestFactory.CardDataGenerator(dbContext, userManager);

                    await cardGen.GenerateAsync();
                    await cardGen.WriteToJsonAsync();
                }

                dbContext.ChangeTracker.Clear();
            }
            finally
            {
                _jsonLock.Release();
            }
        }



        internal static async Task<Deck> CreateDeckAsync(this CardDbContext dbContext, int numCards = default)
        {
            var users = await dbContext.Users.ToListAsync();
            var owner = users[_random.Next(users.Count)];
            var cards = await dbContext.Cards.ToListAsync();

            if (numCards <= 0)
            {
                numCards = _random.Next(1, cards.Count / 2);
            }

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

            var deckAmounts = deckCards
                .Select(c => new DeckAmount
                {
                    Card = c,
                    Location = newDeck,
                    Amount = _random.Next(1, 3)
                });

            dbContext.Attach(newDeck);
            dbContext.DeckAmounts.AttachRange(deckAmounts);

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
                    .Select(deck => new DeckAmount
                    {
                        Card = card,
                        Location = deck
                    });

                dbContext.DeckAmounts.AddRange(amounts);
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
                .Select(card => new DeckAmount
                {
                    Card = card,
                    Location = newDeck,
                    Intent = Intent.Take
                })
                .ToList();

            dbContext.Decks.Attach(newDeck);
            dbContext.DeckAmounts.AttachRange(requests);

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

                trades.Add(new()
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
                toLoc = new("Trade deck")
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
            var fromAmount = await dbContext.DeckAmounts
                .SingleOrDefaultAsync(ca => !ca.HasIntent
                    && ca.CardId == card.Id
                    && ca.LocationId == from.Id);

            if (fromAmount == default)
            {
                fromAmount = new()
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
            var toRequest = await dbContext.DeckAmounts
                .SingleOrDefaultAsync(ca => ca.HasIntent
                    && ca.CardId == card.Id
                    && ca.LocationId == to.Id);

            if (toRequest == default)
            {
                toRequest = new()
                {
                    Card = card,
                    Location = to,
                    Amount = _random.Next(1, maxAmount),
                    Intent = Intent.Take
                };

                dbContext.DeckAmounts.Attach(toRequest);
            }

            return toRequest;
        }
    }
}