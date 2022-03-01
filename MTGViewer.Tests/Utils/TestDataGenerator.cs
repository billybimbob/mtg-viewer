using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Tests.Utils;


public class TestDataGenerator
{
    private static readonly SemaphoreSlim _jsonLock = new(1, 1);

    private readonly CardDbContext _dbContext;
    private readonly UserDbContext _userContext;
    private readonly UserManager<CardUser> _userManager;

    private readonly FileCardStorage _fileStorage;
    private readonly CardDataGenerator _cardGen;

    private readonly Random _random;


    public TestDataGenerator(
        CardDbContext dbContext, 
        UserDbContext userContext,
        UserManager<CardUser> userManager,
        FileCardStorage jsonStorage,
        CardDataGenerator cardGen)
    {
        _dbContext = dbContext;
        _userContext = userContext;
        _userManager = userManager;

        _fileStorage = jsonStorage;
        _cardGen = cardGen;

        _random = new(100);
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
                await _fileStorage.WriteBackupAsync();
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
            await _fileStorage.JsonSeedAsync();
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

        var deckAmounts = deckCards
            .Select(c => new Amount
            {
                Card = c,
                Location = newDeck,
                NumCopies = _random.Next(1, 3)
            });

        _dbContext.Decks.Attach(newDeck);
        _dbContext.Amounts.AttachRange(deckAmounts);

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
            .SelectMany(d => d.Cards)
            .Select(a => a.Card)
            .Distinct()
            .ToListAsync();

        if (cardOptions.Count < 2)
        {
            var optionIds = cardOptions.Select(c => c.Id).ToArray();
            var nonOwner = users.First(u => u.Id != owner.Id);
            var card = await _dbContext.Cards
                .FirstAsync(c => !optionIds.Contains(c.Id));

            var decks = new List<Deck>()
            {
                new Deck { Name = "Source #1", Owner = nonOwner },
                new Deck { Name = "Source #2", Owner = nonOwner }
            };

            var amounts = decks
                .Select(deck => new Amount
                {
                    Card = card,
                    Location = deck
                });

            _dbContext.Amounts.AttachRange(amounts);
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
            .Select(card => new Want
            {
                Card = card,
                Location = newDeck,
                NumCopies = _random.Next(1, 3)
            })
            .ToList();

        _dbContext.Decks.Attach(newDeck);
        _dbContext.Wants.AttachRange(takeRequests);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return newDeck;
    }



    public async Task<TradeSet> CreateTradeSetAsync(bool isToSet)
    {
        var users = await _dbContext.Users
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
        var amountTrades = _random.Next(1, cards.Count / 2);

        var trades = new List<Trade>();

        foreach(var tradeCard in cards.Take(amountTrades))
        {
            var from = froms[_random.Next(froms.Count)];

            int actualAmount = _random.Next(1, 3);
            int requestAmount = _random.Next(1, actualAmount);

            var fromAmount = await FindAmountAsync(tradeCard, from, actualAmount);
            var toRequest = await FindWantAsync(tradeCard, to, requestAmount);

            trades.Add(new()
            {
                Card = tradeCard,
                To = to,
                From = from,
                Amount = toRequest.NumCopies
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
            .Include(d => d.Cards)
                .ThenInclude(a => a.Card)
            .FirstOrDefaultAsync(l => l.OwnerId == sourceUser.Id);

        if (source == default)
        {
            source = new()
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
        var amountTrades = _random.Next(1, cards.Count / 2);

        var trades = new List<Trade>();

        foreach(var tradeCard in cards.Take(amountTrades))
        {
            var to = tos[_random.Next(tos.Count)];

            int actualAmount = _random.Next(1, 3);
            int requestAmount = _random.Next(1, actualAmount);

            var fromAmount = await FindAmountAsync(tradeCard, from, actualAmount);
            var toRequest = await FindWantAsync(tradeCard, to, requestAmount);

            trades.Add(new()
            {
                Card = tradeCard,
                To = to,
                From = from,
                Amount = toRequest.NumCopies
            });
        }

        _dbContext.Trades.AttachRange(trades);

        return trades;
    }


    private async Task<Amount> FindAmountAsync(Card card, Location location, int amount)
    {
        var cardAmount = await _dbContext.Amounts
            .SingleOrDefaultAsync(a =>
                a.LocationId == location.Id && a.CardId == card.Id);

        if (cardAmount == default)
        {
            cardAmount = new()
            {
                Card = card,
                Location = location
            };

            _dbContext.Amounts.Attach(cardAmount);
        }

        cardAmount.NumCopies = amount;

        return cardAmount;
    }


    private async Task<Want> FindWantAsync(Card card, Deck target, int amount)
    {
        var want = await _dbContext.Wants
            .SingleOrDefaultAsync(w => 
                w.LocationId == target.Id && w.CardId == card.Id);

        if (want == default)
        {
            want = new()
            {
                Card = card,
                Location = target,
            };

            _dbContext.Wants.Attach(want);
        }

        want.NumCopies = amount;

        return want;
    }


    private async Task<GiveBack> FindGiveBackAsync(Card card, Deck target, int amount)
    {
        var give = await _dbContext.GiveBacks
            .SingleOrDefaultAsync(g => 
                g.LocationId == target.Id && g.CardId == card.Id);

        if (give == default)
        {
            give = new()
            {
                Card = card,
                Location = target
            };

            _dbContext.GiveBacks.Attach(give);
        }

        give.NumCopies = amount;

        return give;
    }


    public async Task<Want> GetWantAsync(int targetMod = 0)
    {
        var deckTarget = await _dbContext.Decks
            .AsNoTracking()
            .FirstAsync();

        var takeTarget = await _dbContext.Amounts
            .Where(a => a.Location is Box && a.NumCopies > 0)
            .Select(a => a.Card)
            .AsNoTracking()
            .FirstAsync();

        var targetCap = await _dbContext.Amounts
            .Where(a => a.Location is Box && a.CardId == takeTarget.Id)
            .Select(a => a.NumCopies)
            .SumAsync();

        int limit = Math.Max(1, targetCap + targetMod);

        var want = await FindWantAsync(takeTarget, deckTarget, limit);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return want;
    }


    public async Task<GiveBack> GetGiveBackAsync(int targetMod = 0)
    {
        var returnTarget = await _dbContext.Amounts
            .Include(a => a.Card)
            .Include(a => a.Location)
            .AsNoTracking()
            .FirstAsync(a => a.Location is Deck
                && !(a.Location as Deck)!.TradesTo.Any()
                && a.NumCopies > 0);

        int limit = Math.Max(1, returnTarget.NumCopies + targetMod);

        var give = await FindGiveBackAsync(
            returnTarget.Card, (Deck)returnTarget.Location, limit);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return give;
    }


    public async Task<(Want, GiveBack)> GetMixedRequestDeckAsync()
    {
        var returnTarget = await _dbContext.Amounts
            .Include(a => a.Card)
            .Include(a => a.Location)
            .AsNoTracking()
            .FirstAsync(a => a.Location is Deck && a.NumCopies > 0);

        var deckTarget = (Deck)returnTarget.Location;

        var takeTarget = await _dbContext.Amounts
            .Where(a => a.Location is Box 
                && a.NumCopies > 0
                && a.CardId != returnTarget.CardId)
            .Select(a => a.Card)
            .AsNoTracking()
            .FirstAsync();

        var targetCap = await _dbContext.Amounts
            .Where(a => a.Location is Box && a.CardId == takeTarget.Id)
            .Select(a => a.NumCopies)
            .SumAsync();

        var deckGive = await FindGiveBackAsync(
            returnTarget.Card, deckTarget, returnTarget.NumCopies);

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
                Amount = _random.Next(1, 3),
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

        var amounts = targetCards
            .Take(_random.Next(1, targetCards.Count))
            .Select(card => new Amount
            {
                Card = card,
                Location = unclaimed,
                NumCopies = _random.Next(1, 5)
            });

        var wants = targetCards
            .Take(_random.Next(1, targetCards.Count))
            .Select(card => new Want
            {
                Card = card,
                Location = unclaimed,
                NumCopies = _random.Next(1, 5)
            });

        _dbContext.Unclaimed.Attach(unclaimed);
        _dbContext.Amounts.AttachRange(amounts);
        _dbContext.Wants.AttachRange(wants);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return unclaimed;
    }


    public async Task AddExcessAsync(int excessSpace)
    {
        int capacity = await _dbContext.Boxes
            .SumAsync(b => b.Capacity);

        int availAmounts = await _dbContext.Boxes
            .SelectMany(b => b.Cards)
            .SumAsync(a => a.NumCopies);

        if (availAmounts > capacity)
        {
            throw new InvalidOperationException("There are too many cards not in excess");
        }

        var card = await _dbContext.Cards.FirstAsync();

        if (availAmounts < capacity)
        {
            var boxes = await _dbContext.Boxes
                .Include(b => b.Cards)
                .ToListAsync();

            foreach (var box in boxes)
            {
                int remaining = box.Capacity - box.Cards.Sum(a => a.NumCopies);
                if (remaining <= 0)
                {
                    continue;
                }

                if (box.Cards.FirstOrDefault() is Amount amount)
                {
                    amount.NumCopies += remaining;
                    continue;
                }

                amount = new Amount
                {
                    Card = card,
                    Location = box,
                    NumCopies = remaining
                };

                _dbContext.Amounts.Attach(amount);
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

        var excessCard = new Amount
        {
            Card = card,
            Location = excess,
            NumCopies = excessSpace
        };

        _dbContext.Excess.Attach(excess);
        _dbContext.Amounts.Attach(excessCard);

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

        var amounts = card
            .Select(c => new Amount
            {
                Card = c,
                Location = deck,
                NumCopies = _random.Next(4)
            });

        deck.Cards.AddRange(amounts);

        _dbContext.Decks.Attach(deck);

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();
    }
}