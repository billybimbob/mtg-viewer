using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MTGViewer.Data.Triggers;

public class DeckColorUpdate : IBeforeSaveTrigger<Deck>
{
    private readonly CardDbContext _dbContext;
    private readonly ILogger<DeckColorUpdate> _logger;

    public DeckColorUpdate(CardDbContext dbContext, ILogger<DeckColorUpdate> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }


    public async Task BeforeSave(ITriggerContext<Deck> trigContext, CancellationToken cancel)
    {
        if (trigContext.ChangeType is ChangeType.Deleted)
        {
            return;
        }

        var deck = trigContext.Entity;

        if (trigContext.ChangeType is ChangeType.Added
            && (deck.Cards.Any(a => a.Card is null)
                || deck.Wants.Any(w => w.Card is null)))
        {
            var cardIds = deck.Cards.Select(a => a.CardId)
                .Union(deck.Wants.Select(w => w.CardId))
                .ToArray();

            deck.Color = await CardColors
                .Invoke(_dbContext, cardIds)
                .AggregateAsync(Color.None, (color, card) => color | card, cancel);

            return;
        }

        if (trigContext.ChangeType is ChangeType.Added)
        {
            deck.Color = GetColor(deck);
            return;
        }

        if (deck.Cards.Any() && deck.Cards.All(a => a.Card is not null)
            || deck.Wants.Any() && deck.Wants.All(w => w.Card is not null))
        {
            deck.Color = GetColor(deck);
            return;
        }

        var deckEntry = _dbContext.Entry(deck);

        if (deck.Cards.Any(a => a.Card is null)
            || deck.Wants.Any(w => w.Card is null)

            || !deckEntry.Collection(d => d.Cards).IsLoaded
            || !deckEntry.Collection(d => d.Wants).IsLoaded)
        {
            var deckColors = await DeckColorsAsync.Invoke(_dbContext, deck.Id, cancel);

            deck.Color = GetColor(deckColors);
            return;
        }

        deck.Color = GetColor(deck);
    }


    private static Color GetColor(Deck deck)
    {
        var amountColors = deck.Cards
            .Select(a => a.Card.Color);

        var wantColors = deck.Wants
            .Select(w => w.Card.Color);

        return amountColors
            .Concat(wantColors)
            .Aggregate(Color.None, (color, card) => color | card);
    }


    private static Color GetColor(DeckColors? colors)
    {
        if (colors is null)
        {
            return Color.None;
        }

        return colors.CardColors
            .Concat(colors.WantColors)
            .Aggregate(Color.None, (color, card) => color | card);
    }


    private static readonly Func<CardDbContext, int, CancellationToken, Task<DeckColors?>> DeckColorsAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, CancellationToken _) =>
            dbContext.Decks
                .Where(d => d.Id == deckId)
                .Select(d => new DeckColors
                {
                    Id = d.Id,
                    CardColors = d.Cards.Select(a => a.Card.Color),
                    WantColors = d.Wants.Select(w => w.Card.Color)
                })
                .SingleOrDefault());


    private static readonly Func<CardDbContext, string[], IAsyncEnumerable<Color>> CardColors

        = EF.CompileAsyncQuery((CardDbContext dbContext, string[] cardIds) =>
            dbContext.Cards
                .Where(c => cardIds.Contains(c.Id))
                .Select(c => c.Color));
}