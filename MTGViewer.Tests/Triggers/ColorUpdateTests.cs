using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using EntityFrameworkCore.Triggered;
using Moq;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Data.Projections;
using MTGViewer.Triggers;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Triggers;

public class ColorUpdateTests : IAsyncLifetime
{
    private readonly ColorUpdate _colorUpdate;
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;

    public ColorUpdateTests(
        ColorUpdate colorUpdate,
        CardDbContext dbContext,
        TestDataGenerator testGen)
    {
        _colorUpdate = colorUpdate;
        _dbContext = dbContext;
        _testGen = testGen;
    }

    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();

    private static Color GetColor(IEnumerable<Card> cards)
    {
        return cards
            .Aggregate(Color.None, (color, c) => color | c.Color);
    }

    private static Color GetColor(Theorycraft theory)
    {
        var cards = theory.Holds
            .Select(h => h.Card)
            .Concat(theory.Wants
                .Select(w => w.Card));

        return GetColor(cards);
    }

    [Fact]
    public async Task BeforeSave_DeleteDeck_NoChange()
    {
        const Color black = Color.Black;

        var theory = new Deck { Color = black };

        var triggerContext = new Mock<ITriggerContext<Theorycraft>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Deleted);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(theory);

        await _colorUpdate.BeforeSave(triggerContext.Object, default);

        Assert.Equal(theory.Color, black);
    }

    [Fact]
    public async Task BeforeSave_AddDeckWithCards_CorrectColor()
    {
        var cards = await _dbContext.Cards
            .Take(5)
            .AsNoTracking()
            .ToListAsync();

        var color = GetColor(cards);

        var theory = new Deck
        {
            Color = Color.None,

            Holds = cards
                .Take(2)
                .Select(c => new Hold { Card = c })
                .ToList(),

            Wants = cards
                .Skip(2)
                .Select(c => new Want { Card = c })
                .ToList()
        };

        _dbContext.Decks.Attach(theory); // attack for nav fixup

        var triggerContext = new Mock<ITriggerContext<Theorycraft>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Added);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(theory);

        await _colorUpdate.BeforeSave(triggerContext.Object, default);

        Assert.Equal(theory.Color, color);
    }

    [Fact]
    public async Task BeforeSave_AddDeckMissingCards_CorrectColor()
    {
        var cards = await _dbContext.Cards
            .Take(5)
            .AsNoTracking()
            .ToListAsync();

        var color = GetColor(cards);

        var theory = new Deck
        {
            Color = Color.None,

            Holds = cards
                .Take(2)
                .Select(c => new Hold { CardId = c.Id })
                .ToList(),

            Wants = cards
                .Skip(2)
                .Select(c => new Want { CardId = c.Id })
                .ToList()
        };

        _dbContext.Decks.Attach(theory); // attach for nav fixup

        bool cardAreMissing = theory.Wants.All(w => w.Card is null)
            && theory.Holds.All(h => h.Card is null);

        var triggerContext = new Mock<ITriggerContext<Theorycraft>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Added);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(theory);

        await _colorUpdate.BeforeSave(triggerContext.Object, default);

        Assert.True(cardAreMissing);
        Assert.Equal(theory.Color, color);
    }

    [Fact]
    public async Task BeforeSave_UpdateFullyLoadedDeck_CorrectColor()
    {
        var deck = await _dbContext.Decks
            .Include(d => d.Holds)
                .ThenInclude(h => h.Card)
            .Include(d => d.Wants)
                .ThenInclude(w => w.Card)

            .AsNoTrackingWithIdentityResolution()
            .FirstAsync();

        string[] cardIds = deck.Holds
            .Select(h => h.CardId)
            .Concat(deck.Wants
                .Select(w => w.CardId))
            .ToArray();

        var newHolds = await _dbContext.Cards
            .Where(c => !cardIds.Contains(c.Id))
            .Take(4)
            .AsNoTracking()
            .Select(c => new Hold
            {
                Card = c,
                Location = deck
            })
            .ToListAsync();

        deck.Holds.AddRange(newHolds);

        _dbContext.Decks.Attach(deck); // attach for nav fixup

        var color = GetColor(deck);

        var triggerContext = new Mock<ITriggerContext<Theorycraft>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Modified);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(deck);

        await _colorUpdate.BeforeSave(triggerContext.Object, default);

        Assert.Equal(color, deck.Color);
    }

    [Fact]
    public async Task BeforeSave_UpdateDeckMissingCards_CorrectColor()
    {
        var deck = await _dbContext.Decks.FirstAsync();

        var deckColors = await _dbContext.Decks
            .Where(d => d.Id == deck.Id)
            .Select(d => new TheoryColors
            {
                Id = d.Id,
                HoldColors = d.Holds.Select(h => h.Card.Color),
                WantColors = d.Wants.Select(w => w.Card.Color)
            })
            .SingleAsync();

        var newHolds = await _dbContext.Cards
            .Where(c => c.Holds.All(h => h.LocationId != deck.Id)
                && c.Wants.All(w => w.LocationId != deck.Id))
            .Take(4)
            .AsNoTracking()
            .Select(c => new Hold
            {
                Card = c,
                Location = deck
            })
            .ToListAsync();

        deck.Holds.AddRange(newHolds);

        var color = deckColors.HoldColors
            .Union(deckColors.WantColors)
            .Aggregate(GetColor(deck), (color, c) => color | c);

        _dbContext.Decks.Attach(deck); // attach for nav fixup

        var triggerContext = new Mock<ITriggerContext<Theorycraft>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Modified);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(deck);

        await _colorUpdate.BeforeSave(triggerContext.Object, default);

        Assert.Equal(color, deck.Color);
    }

    [Fact]
    public async Task BeforeSave_UpdateDeckNotLoaded_CorrectColor()
    {
        var deck = await _dbContext.Decks
            .Include(d => d.Holds)
            .Include(d => d.Wants)
            .AsNoTrackingWithIdentityResolution()
            .FirstAsync();

        var deckColors = await _dbContext.Decks
            .Where(d => d.Id == deck.Id)
            .Select(d => new TheoryColors
            {
                Id = d.Id,
                HoldColors = d.Holds.Select(h => h.Card.Color),
                WantColors = d.Wants.Select(w => w.Card.Color)
            })
            .SingleAsync();

        string[] cardIds = deck.Holds
            .Select(h => h.CardId)
            .Concat(deck.Wants
                .Select(w => w.CardId))
            .ToArray();

        var newHolds = await _dbContext.Cards
            .Where(c => !cardIds.Contains(c.Id))
            .Take(4)
            .AsNoTracking()
            .Select(c => new Hold
            {
                Card = c,
                Location = deck
            })
            .ToListAsync();

        deck.Holds.AddRange(newHolds);

        _dbContext.Decks.Attach(deck); // attach for nav fixup

        var color = newHolds
            .Select(h => h.Card.Color)
            .Union(deckColors.HoldColors)
            .Union(deckColors.WantColors)
            .Aggregate(Color.None, (color, c) => color | c);

        var triggerContext = new Mock<ITriggerContext<Theorycraft>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Modified);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(deck);

        await _colorUpdate.BeforeSave(triggerContext.Object, default);

        Assert.Equal(color, deck.Color);
    }
}
