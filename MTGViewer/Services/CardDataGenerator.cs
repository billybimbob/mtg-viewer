using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Services
{
    internal class SeedSettings
    {
        public int Value { get; set; } = 100;
        public string Password { get; set; }
    }


    public class CardDataGenerator
    {
        private readonly Random _random;
        private readonly string _seedPassword;

        private readonly MTGFetchService _fetch;

        private readonly CardDbContext _dbContext;
        private readonly ITreasury _treasury;
        private readonly UserManager<CardUser> _userManager;

        public CardDataGenerator(
            IConfiguration config,
            MTGFetchService fetchService,
            CardDbContext dbContext,
            ITreasury treasury,
            UserManager<CardUser> userManager)
        {
            var seed = new SeedSettings();
            config.GetSection(nameof(SeedSettings)).Bind(seed);

            _random = new(seed.Value);
            _seedPassword = seed.Password;

            _fetch = fetchService;

            _dbContext = dbContext;
            _treasury = treasury;
            _userManager = userManager;
        }


        public async Task GenerateAsync(CancellationToken cancel = default)
        {
            var users = GetUsers();
            var userRefs = users.Select(u => new UserRef(u)).ToList();

            var cards = await GetCardsAsync(_fetch);
            var decks = GetDecks(userRefs);
            var boxes = GetBoxes();

            var boxAmounts = GetBoxAmounts(cards);
            var deckAmounts = GetDeckAmounts(cards, decks);

            var trades = GetTrades(userRefs, cards, decks, deckAmounts);
            var suggestions = GetSuggestions(userRefs, cards, decks, deckAmounts);

            _dbContext.Users.AddRange(userRefs);
            _dbContext.Cards.AddRange(cards);

            _dbContext.Decks.AddRange(decks);
            _dbContext.Boxes.AddRange(boxes);

            _dbContext.Amounts.AddRange(deckAmounts);

            _dbContext.Suggestions.AddRange(suggestions);
            _dbContext.Trades.AddRange(trades);

            await _dbContext.SaveChangesAsync(cancel);

            await _treasury.ReturnAsync(boxAmounts);

            // TODO: fix created accounts not being verified
            var results = await Task.WhenAll(
                users.Select(u => _seedPassword != default
                    ? _userManager.CreateAsync(u, _seedPassword)
                    : _userManager.CreateAsync(u)));
        }


        private IReadOnlyList<CardUser> GetUsers() => new List<CardUser>()
        {
            new CardUser
            {
                Name = "Test Name",
                UserName = "testingname",
                Email = "test@gmail.com",
                EmailConfirmed = true
            },
            new CardUser
            {
                Name = "Bob Billy",
                UserName = "bobbilly213",
                Email = "bob@gmail.com",
                EmailConfirmed = true
            },
            new CardUser
            {
                Name = "Steve Phil",
                UserName = "stephenthegreat",
                Email = "steve@gmail.com",
                EmailConfirmed = true
            }
        };


        private async Task<IReadOnlyList<Card>> GetCardsAsync(MTGFetchService fetchService)
        {
            return await fetchService
                .Where(c => c.Cmc, 3)
                .SearchAsync();
        }


        private IReadOnlyList<Box> GetBoxes()
        {
            // just use same bin for now
            var bin = new Bin { Name = "Bin #1" };

            return Enumerable.Range(0, 3)
                .Select(i => new Box
                {
                    Name = $"Box #{i+1}",
                    Bin = bin
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


        private IReadOnlyList<CardAmount> GetDeckAmounts(
            IEnumerable<Card> cards,
            IEnumerable<Deck> decks)
        {
            return cards.Zip(decks,
                (card, deck) => (card, deck))
                .Select(cd => new CardAmount
                {
                    Card = cd.card,
                    Location = cd.deck,
                    Amount = _random.Next(6)
                })
                .ToList();
        }


        private IReadOnlyList<CardReturn> GetBoxAmounts(IEnumerable<Card> cards)
        {
            return cards
                .Select(card => new CardReturn(card, _random.Next(1, 6)))
                .ToList();
        }


        private IReadOnlyList<Trade> GetTrades(
            IEnumerable<UserRef> users,
            IEnumerable<Card> cards,
            IEnumerable<Deck> decks,
            IEnumerable<CardAmount> amounts)
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
            IEnumerable<CardAmount> amounts)
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

    }
}