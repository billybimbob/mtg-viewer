using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using EntityFrameworkCore.Triggered;
using Moq;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Data.Triggers;
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


    public async Task InitializeAsync()
    {
        await _testGen.SeedAsync();
    }


    public async Task DisposeAsync()
    {
        await _testGen.ClearAsync();
    }


    [Fact]
    public async Task BeforeSave_DeleteChange_NoChange()
    {
        const Color black = Color.Black;

        var theory = new Deck { Color = black };

        var triggerContext = new Mock<ITriggerContext<TheoryCraft>>();

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
    public async Task BeforeSave_AddChangeWithCards_CorrectColor()
    {
        var cards = await _dbContext.Cards
            .Take(5)
            .AsNoTracking()
            .ToListAsync();

        var color = cards.Aggregate(Color.None, (color, c) => color | c.Color);

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

        var triggerContext = new Mock<ITriggerContext<TheoryCraft>>();

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
    public async Task BeforeSave_AddChangeMissingCards_CorrectColor()
    {
        var cards = await _dbContext.Cards
            .Take(5)
            .AsNoTracking()
            .ToListAsync();

        var color = cards.Aggregate(Color.None, (color, c) => color | c.Color);

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

        _dbContext.Decks.Attach(theory); // attack for nav fixup

        bool cardAreMissing = theory.Wants.All(w => w.Card is null)
            && theory.Holds.All(h => h.Card is null);

        var triggerContext = new Mock<ITriggerContext<TheoryCraft>>();

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
}