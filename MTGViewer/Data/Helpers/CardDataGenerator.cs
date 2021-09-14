using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using MTGViewer.Areas.Identity.Data;
using MTGViewer.Services;


namespace MTGViewer.Data.Seed
{
    public class CardDataGenerator
    {
        private readonly CardDbContext _dbContext;
        private readonly ISharedStorage _sharedStorage;
        private readonly UserManager<CardUser> _userManager;

        private readonly MTGFetchService _fetch;
        private readonly Random _random;


        public CardDataGenerator(
            CardDbContext dbContext,
            ISharedStorage sharedStorage,
            UserManager<CardUser> userManager,
            MTGFetchService fetchService)
        {
            _dbContext = dbContext;
            _sharedStorage = sharedStorage;
            _userManager = userManager;

            _fetch = fetchService;
            _random = new(100);
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

            var transfers = GetTransfers(userRefs, cards, decks, deckAmounts);

            var suggestions = transfers
                .Where(t => t is Suggestion)
                .Cast<Suggestion>();

            var trades = transfers
                .Where(t => t is Trade)
                .Cast<Trade>();

            _dbContext.Users.AddRange(userRefs);

            _dbContext.Cards.AddRange(cards);
            _dbContext.Decks.AddRange(decks);
            _dbContext.Boxes.AddRange(boxes);

            _dbContext.DeckAmounts.AddRange(deckAmounts);
            _dbContext.Suggestions.AddRange(suggestions);
            _dbContext.Trades.AddRange(trades);

            await _dbContext.SaveChangesAsync(cancel);

            await _sharedStorage.ReturnAsync(boxAmounts);

            // TODO: figure out why passwords are not correct
            await Task.WhenAll(
                users.Select(u => _userManager.CreateAsync(u, Storage.USER_PASSWORD)));
        }


        public Task WriteToJsonAsync(string path = default, CancellationToken cancel = default) =>
            Storage.WriteToJsonAsync(_dbContext, _userManager, path, cancel);


        public Task<bool> AddFromJsonAsync(string path = default, CancellationToken cancel = default) =>
            Storage.AddFromJsonAsync(_dbContext, _userManager, path, cancel);


        private IReadOnlyList<CardUser> GetUsers() => new List<CardUser>()
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


        private async Task<IReadOnlyList<Card>> GetCardsAsync(MTGFetchService fetchService)
        {
            return await fetchService
                .Where(c => c.Cmc, 3)
                .SearchAsync();
        }


        private IReadOnlyList<Box> GetBoxes()
        {
            // just use same bin for now
            var bin = new Bin("Bin #1");

            return Enumerable.Range(0, 3)
                .Select(i => new Box($"Box #{i+1}")
                {
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
                    .Select(i => new Deck($"Deck #{i+1}")
                    {
                        Owner = u
                    }))
                .ToList();
        }


        private IReadOnlyList<DeckAmount> GetDeckAmounts(
            IEnumerable<Card> cards,
            IEnumerable<Deck> decks)
        {
            return cards.Zip(decks,
                (card, deck) => (card, deck))
                .Select(cl => new DeckAmount
                {
                    Card = cl.card,
                    Location = cl.deck,
                    Amount = _random.Next(6)
                })
                .ToList();
        }


        private IReadOnlyList<(Card, int)> GetBoxAmounts(IEnumerable<Card> cards)
        {
            return cards
                .Select(c => (c, _random.Next(6)))
                .ToList();
        }


        private IReadOnlyList<Transfer> GetTransfers(
            IEnumerable<UserRef> users,
            IEnumerable<Card> cards,
            IEnumerable<Deck> decks,
            IEnumerable<CardAmount> amounts)
        {
            var source = amounts.First(ca => ca.Location is Deck);

            // TODO: do not use id comparisons
            var tradeFrom = (Deck)source.Location;
            var tradeTo = decks.First(l => l != source.Location);

            var suggestCard = cards.First();
            var suggester = users.First(u => 
                u != tradeFrom.Owner && u != tradeTo.Owner);

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
    }
}