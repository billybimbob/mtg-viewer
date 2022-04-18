using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MTGViewer.Data.Triggers;

public class ColorUpdate : IBeforeSaveTrigger<Theorycraft>
{
    private readonly CardDbContext _dbContext;
    private readonly ILogger<ColorUpdate> _logger;

    public ColorUpdate(CardDbContext dbContext, ILogger<ColorUpdate> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task BeforeSave(ITriggerContext<Theorycraft> context, CancellationToken cancellationToken)
    {
        if (context.ChangeType is ChangeType.Deleted)
        {
            return;
        }

        var theory = context.Entity;

        if (context.ChangeType is ChangeType.Added)
        {
            await SetAddedColorAsync(theory, cancellationToken);
            return;
        }

        if (IsFullyLoaded(theory))
        {
            theory.Color = GetColor(theory);
            return;
        }

        _logger.LogWarning("Theorycraft {TheoryId} not fully loaded", theory.Id);

        var deckColors
            = await DeckColorsAsync.Invoke(_dbContext, theory.Id, cancellationToken)
            ?? await UnclaimedColorsAsync.Invoke(_dbContext, theory.Id, cancellationToken);

        theory.Color = GetColor(deckColors, theory);
    }

    private async Task SetAddedColorAsync(Theorycraft theory, CancellationToken cancel)
    {
        if (theory.Holds.All(h => h.Card is not null)
            && theory.Wants.All(w => w.Card is not null))
        {
            theory.Color = GetColor(theory);
            return;
        }

        _logger.LogWarning("Theorycraft {TheoryId} not fully loaded", theory.Id);

        var cardIds = theory.Holds.Select(h => h.CardId)
            .Union(theory.Wants.Select(w => w.CardId))
            .ToArray();

        var localColors = theory.Holds
            .Select(h => h.Card?.Color)
            .Concat(theory.Wants
                .Select(w => w.Card?.Color))

            .Distinct()
            .OfType<Color>()
            .ToAsyncEnumerable();

        theory.Color = await CardColorsAsync
            .Invoke(_dbContext, cardIds)
            .Union(localColors)
            .AggregateAsync(Color.None, (color, c) => color | c, cancel);
    }

    private bool IsFullyLoaded(Theorycraft theory)
    {
        var entry = _dbContext.Entry(theory);

        return theory.Holds.All(h => h.Card is not null)
            && theory.Wants.All(w => w.Card is not null)

            && entry.Collection(t => t.Holds)
                is not { IsLoaded: false, IsModified: true }

            && entry.Collection(t => t.Wants)
                is not { IsLoaded: false, IsModified: true };
    }

    private static Color GetColor(Theorycraft theory)
    {
        var holdColors = theory.Holds
            .Select(h => h.Card.Color);

        var wantColors = theory.Wants
            .Select(w => w.Card.Color);

        return holdColors
            .Union(wantColors)
            .Aggregate(Color.None, (color, card) => color | card);
    }

    private static Color GetColor(TheoryColors? colors, Theorycraft theory)
    {
        if (colors is null)
        {
            return Color.None;
        }

        var holdColors = theory.Holds
            .Select(h => h.Card?.Color)
            .OfType<Color>();

        var wantColors = theory.Wants
            .Select(w => w.Card?.Color)
            .OfType<Color>();

        return colors.HoldColors
            .Union(colors.WantColors)
            .Union(holdColors)
            .Union(wantColors)
            .Aggregate(Color.None, (color, card) => color | card);
    }

    private static readonly Func<CardDbContext, int, CancellationToken, Task<TheoryColors?>> DeckColorsAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, CancellationToken _) =>
            dbContext.Decks
                .Where(d => d.Id == deckId)
                .Select(d => new TheoryColors
                {
                    Id = d.Id,
                    HoldColors = d.Holds.Select(h => h.Card.Color),
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
                    HoldColors = u.Holds.Select(h => h.Card.Color),
                    WantColors = u.Wants.Select(w => w.Card.Color)
                })
                .AsSplitQuery()
                .SingleOrDefault());

    private static readonly Func<CardDbContext, string[], IAsyncEnumerable<Color>> CardColorsAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, string[] cardIds) =>
            dbContext.Cards
                .Where(c => cardIds.Contains(c.Id))
                .Select(c => c.Color)
                .Distinct());
}
