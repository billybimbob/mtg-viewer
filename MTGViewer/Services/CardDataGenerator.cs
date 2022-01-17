using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;

namespace MTGViewer.Services;


internal class SeedSettings
{
    public int Value { get; set; } = 100;
    public string Password { get; set; } = string.Empty;
}


public class CardDataGenerator
{
    private readonly Random _random;
    private readonly string _seedPassword;

    private readonly MTGFetchService _fetch;

    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;

    public CardDataGenerator(
        IConfiguration config,
        MTGFetchService fetchService,
        CardDbContext dbContext,
        UserManager<CardUser> userManager)
    {
        var seed = new SeedSettings();
        config.GetSection(nameof(SeedSettings)).Bind(seed);

        _random = new(seed.Value);
        _seedPassword = seed.Password;

        _fetch = fetchService;

        _dbContext = dbContext;
        _userManager = userManager;
    }


    public async Task GenerateAsync(CancellationToken cancel = default)
    {
        var users = GetUsers();
        var userRefs = users.Select(u => new UserRef(u)).ToList();

        var cards = await GetCardsAsync(_fetch, cancel);
        var decks = GetDecks(userRefs);
        var boxes = GetBoxes();

        var boxAmounts = GetBoxAmounts(cards, boxes);
        var deckAmounts = GetDeckAmounts(cards, decks);

        var trades = GetTrades(userRefs, cards, decks, deckAmounts);
        var suggestions = GetSuggestions(userRefs, cards, decks, deckAmounts);

        _dbContext.Users.AddRange(userRefs);
        _dbContext.Cards.AddRange(cards);

        _dbContext.Decks.AddRange(decks);
        _dbContext.Boxes.AddRange(boxes);

        _dbContext.Amounts.AddRange(deckAmounts);
        _dbContext.Amounts.AddRange(boxAmounts);

        _dbContext.Suggestions.AddRange(suggestions);
        _dbContext.Trades.AddRange(trades);

        await _dbContext.SaveChangesAsync(cancel);

        // TODO: fix created accounts not being verified

        await users
            .ToAsyncEnumerable()
            .ForEachAwaitWithCancellationAsync(RegisterUserAsync, cancel);
    }


    private IReadOnlyList<CardUser> GetUsers() => new List<CardUser>()
    {
        new CardUser
        {
            DisplayName = "Test Name",
            UserName = "test@gmail.com",
            Email = "test@gmail.com",
            EmailConfirmed = true
        },
        new CardUser
        {
            DisplayName = "Bob Billy",
            UserName = "bob@gmail.com",
            Email = "bob@gmail.com",
            EmailConfirmed = true
        },
        new CardUser
        {
            DisplayName = "Steve Phil",
            UserName = "steve@gmail.com",
            Email = "steve@gmail.com",
            EmailConfirmed = true
        }
    };


    private async Task<IReadOnlyList<Card>> GetCardsAsync(MTGFetchService fetchService, CancellationToken cancel)
    {
        var cards = await fetchService
            .Where(c => c.Cmc, 3)
            .SearchAsync();

        cancel.ThrowIfCancellationRequested();

        return cards;
    }


    private IReadOnlyList<Box> GetBoxes()
    {
        // just use same bin for now
        var bin = new Bin { Name = "Bin #1" };

        return Enumerable.Range(0, 3)
            .Select(i => new Box
            {
                Name = $"Box #{i+1}",
                Bin = bin,
                Capacity = _random.Next(10, 50)
            })
            .ToList();
    }


    private IReadOnlyList<Deck> GetDecks(IEnumerable<UserRef> users)
    {
        return users
            .Where((_, i) => i % 2 == 0)
            .SelectMany(u => Enumerable
                .Range(0, _random.Next(2, 4))
                .Select(i => new Deck
                {
                    Name = $"Deck #{i+1}",
                    Owner = u
                }))
            .ToList();
    }


    private IReadOnlyList<Amount> GetDeckAmounts(
        IEnumerable<Card> cards,
        IEnumerable<Deck> decks)
    {
        return cards.Zip(decks,
            (card, deck) => (card, deck))
            .Select(cd => new Amount
            {
                Card = cd.card,
                Location = cd.deck,
                NumCopies = _random.Next(6)
            })
            .ToList();
    }


    private IReadOnlyList<Amount> GetBoxAmounts(
        IEnumerable<Card> cards,
        IReadOnlyList<Box> boxes)
    {
        return cards
            .Select(card => new Amount
            {
                Card = card, 
                Location = boxes[_random.Next(boxes.Count)],
                NumCopies = _random.Next(1, 6)
            })
            .ToList();
    }


    private IReadOnlyList<Trade> GetTrades(
        IEnumerable<UserRef> users,
        IEnumerable<Card> cards,
        IEnumerable<Deck> decks,
        IEnumerable<Amount> amounts)
    {
        var source = amounts.First();
        var tradeFrom = (Deck)source.Location;
        var tradeTo = decks.First(d => d != source.Location);

        return new List<Trade>()
        {
            new Trade
            {
                Card = source.Card,
                To = tradeTo,
                From = tradeFrom,
                Amount = _random.Next(5)
            }
        };
    }


    private IReadOnlyList<Suggestion> GetSuggestions(
        IEnumerable<UserRef> users,
        IEnumerable<Card> cards,
        IEnumerable<Deck> decks,
        IEnumerable<Amount> amounts)
    {
        var source = amounts.First();
        var suggestCard = cards.First();
        var tradeTo = decks.First(d => d != source.Location);

        return new List<Suggestion>()
        {
            new Suggestion
            {
                Card = suggestCard,
                Receiver = tradeTo.Owner,
                To = tradeTo
            }
        };
    }


    private async Task<IdentityResult> RegisterUserAsync(CardUser user, CancellationToken cancel)
    {
        var created = _seedPassword != default
            ? await _userManager.CreateAsync(user, _seedPassword)
            : await _userManager.CreateAsync(user);
        
        cancel.ThrowIfCancellationRequested();

        if (!created.Succeeded)
        {
            return created;
        }

        var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
        cancel.ThrowIfCancellationRequested();

        if (!providers.Any())
        {
            return created;
        }

        string token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        cancel.ThrowIfCancellationRequested();

        var confirmed = await _userManager.ConfirmEmailAsync(user, token);
        cancel.ThrowIfCancellationRequested();

        return confirmed;
    }
}