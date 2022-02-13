using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services.Internal;

namespace MTGViewer.Services;


public class SeedSettings
{
    public int Seed { get; set; } = 100;
    public string JsonPath { get; set; } = "cards.json";
    public string? Password { get; set; }
}


public class CardDataGenerator
{
    private readonly Random _random;
    private readonly IMTGQuery _mtgQuery;
    private readonly BulkOperations _bulkOperations;


    public CardDataGenerator(
        IOptions<SeedSettings> seedOptions,
        IMTGQuery mtgQuery,
        BulkOperations bulkOperations)
    {
        _random = new Random(seedOptions.Value.Seed);
        _mtgQuery = mtgQuery;
        _bulkOperations = bulkOperations;
    }


    public async Task GenerateAsync(CancellationToken cancel = default)
    {
        var users = GetUsers();
        var userRefs = users.Select(u => new UserRef(u)).ToList();

        var cards = await GetCardsAsync(cancel);
        var decks = GetDecks(userRefs);
        var bin = GetBin();

        AddBoxAmounts(cards, bin);
        AddDeckAmounts(cards, decks);

        var suggestions = GetSuggestions(userRefs, cards, decks);
        var trades = GetTrades(userRefs, cards, decks);

        var data = new CardData
        {
            Users = users,
            Refs = userRefs,

            Cards = cards,
            Decks = decks,
            Bins = new[] { bin },

            Suggestions = suggestions,
            Trades = trades
        };

        await _bulkOperations.SeedAsync(data, cancel);
    }


    private IReadOnlyList<CardUser> GetUsers() => new List<CardUser>()
    {
        new CardUser
        {
            DisplayName = "Test Name",
            UserName = "test@gmail.com",
            Email = "test@gmail.com",
            IsApproved = true,
            EmailConfirmed = true
        },
        new CardUser
        {
            DisplayName = "Bob Billy",
            UserName = "bob@gmail.com",
            Email = "bob@gmail.com",
            IsApproved = true,
            EmailConfirmed = true
        },
        new CardUser
        {
            DisplayName = "Steve Phil",
            UserName = "steve@gmail.com",
            Email = "steve@gmail.com",
            IsApproved = true,
            EmailConfirmed = true
        }
    };


    private async Task<IReadOnlyList<Card>> GetCardsAsync(CancellationToken cancel)
    {
        var cards = await _mtgQuery
            .Where(c => c.Cmc == 3)
            .Where(c => c.PageSize == 20)
            .SearchAsync(cancel);

        return cards;
    }


    private Bin GetBin() => new Bin
    {
        // just use same bin for now
        Name = "Bin #1",
        Boxes = Enumerable
            .Range(0, 3)
            .Select(i => new Box
            {
                Name = $"Box #{i + 1}",
                Capacity = _random.Next(10, 50)
            })
            .ToList()
    };


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


    private void AddDeckAmounts(IEnumerable<Card> cards, IEnumerable<Deck> decks)
    {
        foreach (var (card, deck) in cards.Zip(decks))
        {
            deck.Cards.Add(new Amount
            {
                Card = card,
                NumCopies = _random.Next(6)
            });
        }
    }


    private void AddBoxAmounts(IEnumerable<Card> cards, Bin bin)
    {
        var boxes = bin.Boxes;
        var boxSpace = boxes.ToDictionary(b => b, _ => 0);

        foreach (var card in cards)
        {
            var box = boxes[_random.Next(boxes.Count)];

            int space = boxSpace.GetValueOrDefault(box);
            int numCopies = _random.Next(1, 6);

            if (space + numCopies > box.Capacity)
            {
                continue;
            }

            box.Cards.Add(new Amount
            {
                Card = card,
                NumCopies = numCopies
            });

            boxSpace[box] = space + numCopies;
        }
    }


    private IReadOnlyList<Trade> GetTrades(
        IEnumerable<UserRef> users,
        IEnumerable<Card> cards,
        IEnumerable<Deck> decks)
    {
        var tradeFrom = decks.First();
        var tradeTo = decks.First(d => d != tradeFrom);
        var card = tradeFrom.Cards.First().Card;

        return new List<Trade>()
        {
            new Trade
            {
                Card = card,
                To = tradeTo,
                From = tradeFrom,
                Amount = _random.Next(5)
            }
        };
    }


    private IReadOnlyList<Suggestion> GetSuggestions(
        IEnumerable<UserRef> users,
        IEnumerable<Card> cards,
        IEnumerable<Deck> decks)
    {
        var suggestCard = cards.First();
        var source = decks.First();
        var tradeTo = decks.First(d => d != source);

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