using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Services.Infrastructure;
using MtgViewer.Services.Search;

namespace MtgViewer.Services.Seed;

public class CardDataGenerator
{
    private readonly Random _random;
    private readonly IMtgQuery _mtgQuery;
    private readonly SeedHandler _seedHandler;

    public CardDataGenerator(
        IOptions<SeedSettings> seedOptions,
        IMtgQuery mtgQuery,
        SeedHandler seedHandler)
    {
        _random = new Random(seedOptions.Value.Seed);
        _mtgQuery = mtgQuery;
        _seedHandler = seedHandler;
    }

    public async Task GenerateAsync(CancellationToken cancel = default)
    {
        var users = GetUsers();
        var players = users.Select(u => new Player(u)).ToList();

        var cards = await GetCardsAsync(cancel);
        var decks = GetDecks(players);
        var bin = GetBin();

        AddBoxHolds(cards, bin);
        AddDeckHolds(cards, decks);

        var suggestions = GetSuggestions(cards, decks);

        AddTrades(decks);

        var data = new CardData
        {
            Users = users,
            Players = players,

            Cards = cards,
            Decks = decks,
            Bins = new[] { bin },

            Suggestions = suggestions,
        };

        await _seedHandler.SeedAsync(data, cancel);
    }

    private static IReadOnlyList<CardUser> GetUsers() => new List<CardUser>()
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
        var search = new CardSearch
        {
            PageSize = 20
        };

        return await _mtgQuery.SearchAsync(search, cancel);
    }

    private Bin GetBin() => new()
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

    private IReadOnlyList<Deck> GetDecks(IEnumerable<Player> players)
    {
        return players
            .Where((_, i) => i % 2 == 0)
            .SelectMany(owner => Enumerable
                .Range(0, _random.Next(2, 4))
                .Select(i => new Deck
                {
                    Name = $"Deck #{i + 1}",
                    Owner = owner
                }))
            .ToList();
    }

    private void AddDeckHolds(IEnumerable<Card> cards, IEnumerable<Deck> decks)
    {
        foreach (var (card, deck) in cards.Zip(decks))
        {
            deck.Holds.Add(new Hold
            {
                Card = card,
                Copies = _random.Next(1, 6)
            });
        }
    }

    private void AddBoxHolds(IEnumerable<Card> cards, Bin bin)
    {
        var boxes = bin.Boxes;
        var boxSpace = boxes.ToDictionary(b => b, _ => 0);

        foreach (var card in cards)
        {
            var box = boxes[_random.Next(boxes.Count)];

            int space = boxSpace.GetValueOrDefault(box);
            int copies = _random.Next(1, 6);

            if (space + copies > box.Capacity)
            {
                continue;
            }

            box.Holds.Add(new Hold
            {
                Card = card,
                Copies = copies
            });

            boxSpace[box] = space + copies;
        }
    }

    private void AddTrades(IEnumerable<Deck> decks)
    {
        var tradeFrom = decks.First();
        var tradeTo = decks.First(d => d != tradeFrom);
        var card = tradeFrom.Holds.First().Card;

        var trade = new Trade
        {
            Card = card,
            To = tradeTo,
            From = tradeFrom,
            Copies = _random.Next(1, 5)
        };

        tradeTo.TradesTo.Add(trade);
        tradeFrom.TradesFrom.Add(trade);
    }

    private static IReadOnlyList<Suggestion> GetSuggestions(
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
