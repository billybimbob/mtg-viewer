using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services.Infrastructure;
using MTGViewer.Services.Seed;

namespace MTGViewer.Tests.Utils;

public class TestDataGenerator
{
    private static readonly SemaphoreSlim _jsonLock = new(1, 1);

    private readonly CardDbContext _dbContext;
    private readonly UserDbContext _userContext;

    private readonly SeedHandler _seedHandler;
    private readonly CardDataGenerator _cardGen;

    private readonly Random _random;

    public TestDataGenerator(
        CardDbContext dbContext,
        UserDbContext userContext,
        SeedHandler seedHandler,
        CardDataGenerator cardGen)
    {
        _dbContext = dbContext;
        _userContext = userContext;

        _seedHandler = seedHandler;
        _cardGen = cardGen;

        _random = new Random(100);
    }

    public async Task SeedAsync()
    {
        await _jsonLock.WaitAsync();
        try
        {
            await SetupAsync();

            bool jsonSuccess = await TryJsonSeedAsync();

            if (!jsonSuccess)
            {
                await _cardGen.GenerateAsync();
                await _seedHandler.WriteBackupAsync();
            }

            _dbContext.ChangeTracker.Clear();
        }
        finally
        {
            _jsonLock.Release();
        }
    }

    private async Task<bool> TryJsonSeedAsync()
    {
        try
        {
            await _seedHandler.SeedAsync();
            return true;
        }
        catch (System.IO.FileNotFoundException)
        {
            return false;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    public async Task SetupAsync()
    {
        if (_dbContext.Database.IsRelational())
        {
            await _dbContext.Database.EnsureCreatedAsync();
        }

        if (_userContext.Database.IsRelational())
        {
            await _userContext.Database.EnsureCreatedAsync();
        }
    }

    public async Task ClearAsync()
    {
        await _userContext.Database.EnsureDeletedAsync();
        await _dbContext.Database.EnsureDeletedAsync();
    }

    public async Task<Deck> CreateEmptyDeckAsync()
    {
        var users = await _dbContext.Users.ToListAsync();
        var owner = users[_random.Next(users.Count)];

        var newDeck = new Deck
        {
            Name = "Test Deck",
            Owner = owner
        };

        _dbContext.Decks.Attach(newDeck);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return newDeck;
    }

    public async Task<Deck> CreateDeckAsync(int numCards = 0)
    {
        var users = await _dbContext.Users.ToListAsync();
        var owner = users[_random.Next(users.Count)];
        var cards = await _dbContext.Cards.ToListAsync();

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

        var deckHolds = deckCards
            .Zip(
                GetHoldCopies(deckCards.Count, numCards),
                (card, copies) => (card, copies))
            .Select(cc => new Hold
            {
                Card = cc.card,
                Location = newDeck,
                Copies = cc.copies
            });

        _dbContext.Decks.Attach(newDeck);
        _dbContext.Holds.AttachRange(deckHolds);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return newDeck;
    }

    private IEnumerable<int> GetHoldCopies(int numHolds, int targetTotal)
    {
        if (numHolds <= 0)
        {
            yield break;
        }

        if (numHolds >= targetTotal)
        {
            for (int i = 0; i < numHolds; ++i)
            {
                yield return _random.Next(1, 3);
            }

            yield break;
        }

        int perHold = (int)MathF.Ceiling((float)targetTotal / numHolds);

        for (int i = 0; i < numHolds; ++i)
        {
            yield return perHold;
        }
    }

    public async Task<Deck> CreateReturnDeckAsync(int numCards = 0)
    {
        var users = await _dbContext.Users.ToListAsync();
        var owner = users[_random.Next(users.Count)];
        var cards = await _dbContext.Cards.ToListAsync();

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

        var deckHolds = deckCards
            .Select(c => new Hold
            {
                Card = c,
                Location = newDeck,
                Copies = _random.Next(1, 3)
            });

        _dbContext.Decks.Attach(newDeck);
        _dbContext.Holds.AttachRange(deckHolds);

        var deckReturns = newDeck.Holds
            .Select(h => new Giveback
            {
                Card = h.Card,
                Location = newDeck,
                Copies = _random.Next(1, h.Copies)
            });

        _dbContext.Givebacks.AttachRange(deckReturns);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return newDeck;
    }

    public async Task<Deck> CreateWantDeckAsync(int numCards = 0)
    {
        var users = await _dbContext.Users.ToListAsync();
        var owner = users[_random.Next(users.Count)];
        var cards = await _dbContext.Cards.ToListAsync();

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

        var deckWants = deckCards
            .Select(c => new Want
            {
                Card = c,
                Location = newDeck,
                Copies = _random.Next(1, 3)
            });

        _dbContext.Decks.Attach(newDeck);
        _dbContext.Wants.AttachRange(deckWants);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return newDeck;
    }

    public async Task<Deck> CreateRequestDeckAsync()
    {
        var users = await _dbContext.Users.ToListAsync();
        var owner = users[_random.Next(users.Count)];

        var cardOptions = await _dbContext.Decks
            .Where(d => d.OwnerId != owner.Id)
            .SelectMany(d => d.Holds)
            .Select(h => h.Card)
            .Distinct()
            .ToListAsync();

        if (cardOptions.Count < 2)
        {
            string[] optionIds = cardOptions.Select(c => c.Id).ToArray();

            var nonOwner = users.First(u => u.Id != owner.Id);
            var card = await _dbContext.Cards
                .FirstAsync(c => !optionIds.Contains(c.Id));

            var decks = new List<Deck>()
            {
                new Deck { Name = "Source #1", Owner = nonOwner },
                new Deck { Name = "Source #2", Owner = nonOwner }
            };

            var holds = decks
                .Select(deck => new Hold
                {
                    Card = card,
                    Location = deck
                });

            _dbContext.Holds.AttachRange(holds);
            cardOptions.Add(card);
        }

        int numRequests = _random.Next(1, cardOptions.Count / 2);

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

        var wants = targetCards
            .Select(card => new Want
            {
                Card = card,
                Location = newDeck,
                Copies = _random.Next(1, 3)
            })
            .ToList();

        _dbContext.Decks.Attach(newDeck);
        _dbContext.Wants.AttachRange(wants);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return newDeck;
    }

    public async Task<TradeSet> CreateTradeSetAsync(bool isToSet)
    {
        var users = await _dbContext.Users
            .ToListAsync();

        var participants = users
            .Select(user => (user, key: _random.Next(users.Count)))
            .OrderBy(uk => uk.key)
            .Take(2)
            .Select(uk => uk.user)
            .ToList();

        var proposer = participants[0];
        var receiver = participants[1];

        var trades = isToSet
            ? await CreateToTradesAsync(proposer, receiver)
            : await CreateFromTradesAsync(proposer, receiver);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return new TradeSet(trades, isToSet);
    }

    private record TradeOptions(Deck Source, IReadOnlyList<Deck> Options);

    private async Task<IReadOnlyList<Trade>> CreateToTradesAsync(
        UserRef proposer,
        UserRef receiver)
    {
        var (to, froms) = await GetTradeOptionsAsync(proposer, receiver);

        var cards = await _dbContext.Cards.ToListAsync();
        int tradeCount = _random.Next(1, cards.Count / 2);

        var trades = new List<Trade>();

        foreach (var tradeCard in cards.Take(tradeCount))
        {
            var from = froms[_random.Next(froms.Count)];

            int holdCopies = _random.Next(1, 3);
            int wantCopies = _random.Next(1, holdCopies);

            await FindHoldAsync(tradeCard, from, holdCopies);

            var toWant = await FindWantAsync(tradeCard, to, wantCopies);

            trades.Add(new Trade
            {
                Card = tradeCard,
                To = to,
                From = from,
                Copies = toWant.Copies
            });
        }

        _dbContext.Trades.AttachRange(trades);

        return trades;
    }

    private async Task<TradeOptions> GetTradeOptionsAsync(
        UserRef sourceUser,
        UserRef optionsUser)
    {
        var source = await _dbContext.Decks
            .Include(d => d.Holds)
                .ThenInclude(h => h.Card)
            .FirstOrDefaultAsync(l => l.OwnerId == sourceUser.Id);

        if (source == default)
        {
            source = new Deck
            {
                Name = "Trade deck",
                Owner = sourceUser
            };

            _dbContext.Attach(source);
        }

        var options = await _dbContext.Decks
            .Where(l => l.OwnerId == optionsUser.Id)
            .ToListAsync();

        if (!options.Any())
        {
            var option = new Deck
            {
                Name = "Trade deck",
                Owner = optionsUser
            };

            _dbContext.Decks.Attach(option);
            options.Add(option);
        }

        return new TradeOptions(source, options);
    }

    private async Task<IReadOnlyList<Trade>> CreateFromTradesAsync(
        UserRef proposer,
        UserRef receiver)
    {
        var (from, tos) = await GetTradeOptionsAsync(receiver, proposer);

        var cards = await _dbContext.Cards.ToListAsync();
        int tradeCount = _random.Next(1, cards.Count / 2);

        var trades = new List<Trade>();

        foreach (var tradeCard in cards.Take(tradeCount))
        {
            var to = tos[_random.Next(tos.Count)];

            int holdCopies = _random.Next(1, 3);
            int wantCopies = _random.Next(1, holdCopies);

            await FindHoldAsync(tradeCard, from, holdCopies);

            var toWant = await FindWantAsync(tradeCard, to, wantCopies);

            trades.Add(new Trade
            {
                Card = tradeCard,
                To = to,
                From = from,
                Copies = toWant.Copies
            });
        }

        _dbContext.Trades.AttachRange(trades);

        return trades;
    }

    private async Task<Hold> FindHoldAsync(Card card, Location location, int copies)
    {
        var hold = await _dbContext.Holds
            .SingleOrDefaultAsync(h =>
                h.LocationId == location.Id && h.CardId == card.Id);

        if (hold == default)
        {
            hold = new Hold
            {
                Card = card,
                Location = location
            };

            _dbContext.Holds.Attach(hold);
        }

        hold.Copies = copies;

        return hold;
    }

    private async Task<Want> FindWantAsync(Card card, Deck target, int copies)
    {
        var want = await _dbContext.Wants
            .SingleOrDefaultAsync(w =>
                w.LocationId == target.Id && w.CardId == card.Id);

        if (want == default)
        {
            want = new Want
            {
                Card = card,
                Location = target,
            };

            _dbContext.Wants.Attach(want);
        }

        want.Copies = copies;

        return want;
    }

    private async Task<Giveback> FindGivebackAsync(Card card, Deck target, int copies)
    {
        var give = await _dbContext.Givebacks
            .SingleOrDefaultAsync(g =>
                g.LocationId == target.Id && g.CardId == card.Id);

        if (give == default)
        {
            give = new Giveback
            {
                Card = card,
                Location = target
            };

            _dbContext.Givebacks.Attach(give);
        }

        give.Copies = copies;

        return give;
    }

    public async Task<Want> GetWantAsync(int targetMod = 0)
    {
        var deckTarget = await _dbContext.Decks
            .AsNoTracking()
            .FirstAsync();

        var takeTarget = await _dbContext.Holds
            .Where(h => h.Location is Box && h.Copies > 0)
            .Select(h => h.Card)
            .AsNoTracking()
            .FirstAsync();

        int targetCap = await _dbContext.Holds
            .Where(h => h.Location is Box && h.CardId == takeTarget.Id)
            .Select(h => h.Copies)
            .SumAsync();

        int limit = Math.Max(1, targetCap + targetMod);

        var want = await FindWantAsync(takeTarget, deckTarget, limit);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return want;
    }

    public async Task<Giveback> GetGivebackAsync(int targetMod = 0)
    {
        var returnTarget = await _dbContext.Decks
            .Where(d => !d.TradesTo.Any())
            .SelectMany(d => d.Holds)

            .Include(h => h.Card)
            .Include(h => h.Location)

            .AsNoTracking()
            .FirstAsync(h => h.Copies > 0);

        int limit = Math.Max(1, returnTarget.Copies + targetMod);

        var give = await FindGivebackAsync(
            returnTarget.Card, (Deck)returnTarget.Location, limit);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return give;
    }

    public async Task<(Want, Giveback)> GetMixedRequestDeckAsync()
    {
        var returnTarget = await _dbContext.Holds
            .Include(h => h.Card)
            .Include(h => h.Location)
            .AsNoTracking()
            .FirstAsync(h => h.Location is Deck && h.Copies > 0);

        var deckTarget = (Deck)returnTarget.Location;

        var takeTarget = await _dbContext.Holds
            .Where(h => h.Location is Box
                && h.Copies > 0
                && h.CardId != returnTarget.CardId)
            .Select(h => h.Card)
            .AsNoTracking()
            .FirstAsync();

        int targetCap = await _dbContext.Holds
            .Where(h => h.Location is Box && h.CardId == takeTarget.Id)
            .Select(h => h.Copies)
            .SumAsync();

        var deckGive = await FindGivebackAsync(
            returnTarget.Card, deckTarget, returnTarget.Copies);

        var deckWant = await FindWantAsync(
            takeTarget, deckTarget, targetCap);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return (deckWant, deckGive);
    }

    public async Task<Transaction> CreateTransactionAsync(int numCards = 0)
    {
        var transaction = new Transaction();

        var boxes = await _dbContext.Boxes.ToListAsync();
        var cards = await _dbContext.Cards.ToListAsync();
        var deck = await _dbContext.Decks.FirstAsync();

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
                Copies = _random.Next(1, 3),
                Transaction = transaction
            });

        _dbContext.Transactions.Attach(transaction);
        _dbContext.Changes.AttachRange(changes);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return transaction;
    }

    public async Task<Unclaimed> CreateUnclaimedAsync(int numCards = 0)
    {
        var cards = await _dbContext.Cards.ToListAsync();

        if (numCards <= 0)
        {
            numCards = _random.Next(1, cards.Count / 2);
        }

        var targetCards = cards
            .Select(card => (card, key: _random.Next(cards.Count)))
            .OrderBy(ck => ck.key)
            .Take(numCards)
            .Select(ck => ck.card)
            .ToList();

        var unclaimed = new Unclaimed()
        {
            Name = "Test Unclaimed"
        };

        var holds = targetCards
            .Take(_random.Next(1, targetCards.Count))
            .Select(card => new Hold
            {
                Card = card,
                Location = unclaimed,
                Copies = _random.Next(1, 5)
            });

        var wants = targetCards
            .Take(_random.Next(1, targetCards.Count))
            .Select(card => new Want
            {
                Card = card,
                Location = unclaimed,
                Copies = _random.Next(1, 5)
            });

        _dbContext.Unclaimed.Attach(unclaimed);
        _dbContext.Holds.AttachRange(holds);
        _dbContext.Wants.AttachRange(wants);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return unclaimed;
    }

    public async Task AddExcessAsync(int excessSpace)
    {
        int capacity = await _dbContext.Boxes
            .SumAsync(b => b.Capacity);

        int availableHolds = await _dbContext.Boxes
            .SelectMany(b => b.Holds)
            .SumAsync(h => h.Copies);

        if (availableHolds > capacity)
        {
            throw new InvalidOperationException("There are too many cards not in excess");
        }

        var card = await _dbContext.Cards.FirstAsync();

        if (availableHolds < capacity)
        {
            var boxes = await _dbContext.Boxes
                .Include(b => b.Holds)
                .ToListAsync();

            foreach (var box in boxes)
            {
                int remaining = box.Capacity - box.Holds.Sum(h => h.Copies);
                if (remaining <= 0)
                {
                    continue;
                }

                if (box.Holds.FirstOrDefault() is Hold hold)
                {
                    hold.Copies += remaining;
                    continue;
                }

                hold = new Hold
                {
                    Card = card,
                    Location = box,
                    Copies = remaining
                };

                _dbContext.Holds.Attach(hold);
            }
        }

        if (await _dbContext.Excess.AnyAsync())
        {
            return;
        }

        var excess = new Excess
        {
            Name = "Excess",
        };

        var excessCard = new Hold
        {
            Card = card,
            Location = excess,
            Copies = excessSpace
        };

        _dbContext.Excess.Attach(excess);
        _dbContext.Holds.Attach(excessCard);

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();
    }

    public async Task CreateChangesAsync()
    {
        var cards = await _dbContext.Cards
            .Take(_random.Next(1, 10))
            .ToListAsync();

        var additions = cards
            .Select(c => new CardRequest(c, _random.Next(1, 8)));

        await _dbContext.AddCardsAsync(additions);

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();

        var want = await GetWantAsync(5);

        var deck = await _dbContext.Decks
            .SingleAsync(d => d.Id == want.LocationId);

        await _dbContext.ExchangeAsync(deck);

        _dbContext.ChangeTracker.Clear();
    }

    public async Task AddUserCardsAsync(UserRef user)
    {
        var card = await _dbContext.Cards
            .Take(_random.Next(1, 4))
            .ToListAsync();

        var deck = new Deck
        {
            Name = "Test Deck",
            Owner = user
        };

        var holds = card
            .Select(c => new Hold
            {
                Card = c,
                Location = deck,
                Copies = _random.Next(4)
            });

        deck.Holds.AddRange(holds);

        _dbContext.Decks.Attach(deck);

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();
    }
}
