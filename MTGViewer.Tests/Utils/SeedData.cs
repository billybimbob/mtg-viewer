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



        internal static async Task<Deck> CreateDeckAsync(this CardDbContext dbContext, int numCards = 0)
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

            var newDeck = new Deck
            {
                Name = "Test Deck",
                Owner = owner
            };

            var deckAmounts = deckCards
                .Select(c => new CardAmount
                {
                    Card = c,
                    Location = newDeck,
                    Amount = _random.Next(1, 3)
                });

            dbContext.Decks.Attach(newDeck);
            dbContext.Amounts.AttachRange(deckAmounts);

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
                    new Deck { Name = "Source #1" },
                    new Deck { Name = "Source #2" }
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

            var newDeck = new Deck
            {
                Name = "Request Deck",
                Owner = owner
            };

            var takeRequests = targetCards
                .Select(card => new CardRequest
                {
                    Card = card,
                    Target = newDeck,
                    IsReturn = false,
                    Amount = _random.Next(1, 3)
                })
                .ToList();

            dbContext.Decks.Attach(newDeck);
            dbContext.Requests.AttachRange(takeRequests);

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            return newDeck;
        }



        internal static async Task<TradeSet> CreateTradeSetAsync(
            this CardDbContext dbContext, bool isToSet)
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

            var trades = isToSet
                ? await dbContext.CreateToTradesAsync(proposer, receiver)
                : await dbContext.CreateFromTradesAsync(proposer, receiver);

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            return new TradeSet(trades, isToSet);
        }



        private record TradeOptions(Deck Source, IReadOnlyList<Deck> Options) { }


        private static async Task<IReadOnlyList<Trade>> CreateToTradesAsync(
            this CardDbContext dbContext,
            UserRef proposer, 
            UserRef receiver)
        {
            var (to, froms) = await dbContext.GetTradeOptionsAsync(proposer, receiver);

            var cards = await dbContext.Cards.ToListAsync();
            var amountTrades = _random.Next(1, cards.Count / 2);

            var trades = new List<Trade>();

            foreach(var tradeCard in cards.Take(amountTrades))
            {
                var from = froms[_random.Next(froms.Count)];

                int actualAmount = _random.Next(1, 3);
                int requestAmount = _random.Next(1, actualAmount);

                var fromAmount = await dbContext.FindAmountAsync(
                    tradeCard, from, actualAmount);

                var toRequest = await dbContext.FindRequestAsync(
                    tradeCard, to, isReturn: false, requestAmount);

                trades.Add(new()
                {
                    Card = tradeCard,
                    To = to,
                    From = from,
                    Amount = toRequest.Amount
                });
            }

            dbContext.Trades.AddRange(trades);

            return trades;
        }


        private static async Task<TradeOptions> GetTradeOptionsAsync(
            this CardDbContext dbContext,
            UserRef sourceUser, 
            UserRef optionsUser)
        {
            var source = await dbContext.Decks
                .Include(l => l.Cards)
                    .ThenInclude(ca => ca.Card)
                .FirstOrDefaultAsync(l => l.OwnerId == sourceUser.Id);

            if (source == default)
            {
                source = new()
                {
                    Name = "Trade deck",
                    Owner = sourceUser
                };

                dbContext.Attach(source);
            }

            var options = await dbContext.Decks
                .Where(l => l.OwnerId == optionsUser.Id)
                .ToListAsync();

            if (!options.Any())
            {
                var option = new Deck
                {
                    Name = "Trade deck",
                    Owner = optionsUser
                };

                dbContext.Decks.Attach(option);
                options.Add(option);
            }

            return new TradeOptions(source, options);
        }


        private static async Task<IReadOnlyList<Trade>> CreateFromTradesAsync(
            this CardDbContext dbContext,
            UserRef proposer, 
            UserRef receiver)
        {
            var (from, tos) = await dbContext.GetTradeOptionsAsync(receiver, proposer);

            var cards = await dbContext.Cards.ToListAsync();
            var amountTrades = _random.Next(1, cards.Count / 2);

            var trades = new List<Trade>();

            foreach(var tradeCard in cards.Take(amountTrades))
            {
                var to = tos[_random.Next(tos.Count)];

                int actualAmount = _random.Next(1, 3);
                int requestAmount = _random.Next(1, actualAmount);

                var fromAmount = await dbContext.FindAmountAsync(
                    tradeCard, from, actualAmount);

                var toRequest = await dbContext.FindRequestAsync(
                    tradeCard, to, isReturn: false, requestAmount);

                trades.Add(new()
                {
                    Card = tradeCard,
                    To = to,
                    From = from,
                    Amount = toRequest.Amount
                });
            }

            dbContext.Trades.AddRange(trades);

            return trades;
        }


        private static async Task<CardAmount> FindAmountAsync(
            this CardDbContext dbContext,
            Card card, Location location, int amount)
        {
            var cardAmount = await dbContext.Amounts
                .SingleOrDefaultAsync(ca =>
                    ca.LocationId == location.Id && ca.CardId == card.Id);

            if (cardAmount == default)
            {
                cardAmount = new()
                {
                    Card = card,
                    Location = location
                };

                dbContext.Amounts.Attach(cardAmount);
            }

            cardAmount.Amount = amount;

            return cardAmount;
        }


        private static async Task<CardRequest> FindRequestAsync(
            this CardDbContext dbContext,
            Card card, Deck target, bool isReturn, int amount)
        {
            var request = await dbContext.Requests
                .SingleOrDefaultAsync(cr => cr.IsReturn == isReturn
                    && cr.TargetId == target.Id
                    && cr.CardId == card.Id);

            if (request == default)
            {
                request = new()
                {
                    Card = card,
                    Target = target,
                    IsReturn = isReturn,
                };

                dbContext.Requests.Attach(request);
            }

            request.Amount = amount;

            return request;
        }


        internal static async Task<CardRequest> GetTakeRequestAsync(
            this CardDbContext dbContext, int targetMod = 0)
        {
            var deckTarget = await dbContext.Decks
                .AsNoTracking()
                .FirstAsync();

            var takeTarget = await dbContext.Amounts
                .Where(ca => ca.Location is Box && ca.Amount > 0)
                .Select(ca => ca.Card)
                .AsNoTracking()
                .FirstAsync();

            var targetCap = await dbContext.Amounts
                .Where(ca => ca.Location is Box && ca.CardId == takeTarget.Id)
                .Select(ca => ca.Amount)
                .SumAsync();

            int limit = Math.Max(1, targetCap + targetMod);

            var deckTake = await dbContext.FindRequestAsync(
                takeTarget, deckTarget, isReturn: false, limit);

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            return deckTake;
        }


        internal static async Task<CardRequest> GetReturnRequestAsync(
            this CardDbContext dbContext, int targetMod = 0)
        {
            var returnTarget = await dbContext.Amounts
                .Include(ca => ca.Card)
                .Include(ca => ca.Location)
                .AsNoTracking()
                .FirstAsync(ca => ca.Location is Deck && ca.Amount > 0);

            int limit = Math.Max(1, returnTarget.Amount + targetMod);

            var deckReturn = await dbContext.FindRequestAsync(
                returnTarget.Card, (Deck)returnTarget.Location, isReturn: true, limit);

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            return deckReturn;
        }


        internal static async Task<Transaction> CreateTransactionAsync(
            this CardDbContext dbContext, int numCards = 0)
        {
            var transaction = new Transaction();

            var boxes = await dbContext.Boxes.ToListAsync();
            var cards = await dbContext.Cards.ToListAsync();
            var deck = await dbContext.Decks.FirstAsync();

            if (numCards <= 0)
            {
                numCards = _random.Next(1, cards.Count / 2);
            }

            var changeCards = cards
                .Select(card => (card, key: _random.Next(cards.Count)))
                .OrderBy(ck => ck.key)
                .Take(numCards)
                .Select(ck => ck.card)
                .ToList();

            var changes = changeCards
                .Select(card => new Change
                {
                    Card = card,
                    To = deck,
                    From = boxes[_random.Next(boxes.Count)],
                    Amount = _random.Next(1, 3),
                    Transaction = transaction
                });

            dbContext.Transactions.Attach(transaction);
            dbContext.Changes.AttachRange(changes);

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            return transaction;
        }
    }
}