using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MTGViewer.Data.Triggers;

public class ColorUpdate : IBeforeSaveTrigger<TheoryCraft>
{
    private readonly CardDbContext _dbContext;
    private readonly ILogger<ColorUpdate> _logger;

    public ColorUpdate(CardDbContext dbContext, ILogger<ColorUpdate> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }


    public async Task BeforeSave(ITriggerContext<TheoryCraft> trigContext, CancellationToken cancel)
    {
        if (trigContext.ChangeType is ChangeType.Deleted)
        {
            return;
        }

        var theory = trigContext.Entity;

        if (trigContext.ChangeType is ChangeType.Added
            && (theory.Cards.Any(a => a.Card is null)
                || theory.Wants.Any(w => w.Card is null)))
        {
            var cardIds = theory.Cards.Select(a => a.CardId)
                .Union(theory.Wants.Select(w => w.CardId))
                .ToArray();

            theory.Color = await CardColors
                .Invoke(_dbContext, cardIds)
                .AggregateAsync(Color.None, (color, card) => color | card, cancel);

            return;
        }

        if (trigContext.ChangeType is ChangeType.Added)
        {
            theory.Color = GetColor(theory);
            return;
        }

        if (theory.Cards.Any() && theory.Cards.All(a => a.Card is not null)
            || theory.Wants.Any() && theory.Wants.All(w => w.Card is not null))
        {
            theory.Color = GetColor(theory);
            return;
        }

        var theoryEntry = _dbContext.Entry(theory);

        if (theory.Cards.Any(a => a.Card is null)
            || theory.Wants.Any(w => w.Card is null)
            || !theoryEntry.Collection(d => d.Cards).IsLoaded
            || !theoryEntry.Collection(d => d.Wants).IsLoaded)
        {
            var deckColors = await DeckColorsAsync.Invoke(_dbContext, theory.Id, cancel)
                ?? await UnclaimedColorsAsync.Invoke(_dbContext, theory.Id, cancel);

            theory.Color = GetColor(deckColors);
            return;
        }

        theory.Color = GetColor(theory);
    }


    private static Color GetColor(TheoryCraft theory)
    {
        var amountColors = theory.Cards
            .Select(a => a.Card.Color);

        var wantColors = theory.Wants
            .Select(w => w.Card.Color);

        return amountColors
            .Concat(wantColors)
            .Aggregate(Color.None, (color, card) => color | card);
    }


    private static Color GetColor(TheoryColors? colors)
    {
        if (colors is null)
        {
            return Color.None;
        }

        return colors.CardColors
            .Concat(colors.WantColors)
            .Aggregate(Color.None, (color, card) => color | card);
    }


    private static readonly Func<CardDbContext, int, CancellationToken, Task<TheoryColors?>> DeckColorsAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, CancellationToken _) =>
            dbContext.Decks
                .Where(d => d.Id == deckId)
                .Select(d => new TheoryColors
                {
                    Id = d.Id,
                    CardColors = d.Cards.Select(a => a.Card.Color),
                    WantColors = d.Wants.Select(w => w.Card.Color)
                })
                .AsSplitQuery()
                .SingleOrDefault());


    private static readonly Func<CardDbContext, int, CancellationToken, Task<TheoryColors?>> UnclaimedColorsAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, int unclaimedId, CancellationToken _) =>
            dbContext.Unclaimed
                .Where(u => u.Id == unclaimedId)
                .Select(u => new TheoryColors
                {
                    Id = u.Id,
                    CardColors = u.Cards.Select(a => a.Card.Color),
                    WantColors = u.Wants.Select(w => w.Card.Color)
                })
                .AsSplitQuery()
                .SingleOrDefault());


    private static readonly Func<CardDbContext, string[], IAsyncEnumerable<Color>> CardColors

        = EF.CompileAsyncQuery((CardDbContext dbContext, string[] cardIds) =>
            dbContext.Cards
                .Where(c => cardIds.Contains(c.Id))
                .Select(c => c.Color));
}