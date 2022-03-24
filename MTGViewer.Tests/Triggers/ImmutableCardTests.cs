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

public class ImmutableCardTests : IAsyncLifetime
{
    private readonly ImmutableCard _immutableCard;
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;

    public ImmutableCardTests(
        ImmutableCard immutableCard,
        CardDbContext dbContext,
        TestDataGenerator testGen)
    {
        _immutableCard = immutableCard;
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
    public async Task BeforeSave_AddCard_Returns()
    {
        const string cardId = "this is a card id";

        var card = new Card { Id = cardId };

        _dbContext.Cards.Add(card);

        var triggerContext = new Mock<ITriggerContext<Card>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Added);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(card);

        await _immutableCard.BeforeSave(triggerContext.Object, default);

        var afterState = _dbContext.Entry(card).State;

        Assert.Equal(EntityState.Added, afterState);
    }


    [Fact]
    public async Task BeforeSave_RemoveCard_Returns()
    {
        var card = await _dbContext.Cards.FirstAsync();

        _dbContext.Cards.Remove(card);

        var triggerContext = new Mock<ITriggerContext<Card>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Deleted);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(card);

        await _immutableCard.BeforeSave(triggerContext.Object, default);

        var afterState = _dbContext.Entry(card).State;

        Assert.Equal(EntityState.Deleted, afterState);
    }


    [Fact]
    public async Task BeforeSave_UpdateTrackedCard_NoChange()
    {
        var card = await _dbContext.Cards.FirstAsync();

        var beforeState = _dbContext.Cards.Update(card).State;

        var triggerContext = new Mock<ITriggerContext<Card>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Modified);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(card);

        triggerContext
            .SetupGet(t => t.UnmodifiedEntity)
            .Returns(card);

        await _immutableCard.BeforeSave(triggerContext.Object, default);

        var afterState = _dbContext.Entry(card).State;

        Assert.Equal(EntityState.Modified, beforeState);
        Assert.Equal(EntityState.Unchanged, afterState);
    }


    [Fact]
    public async Task BeforeSave_UpdateUntrackedCard_Throws()
    {
        var card = await _dbContext.Cards
            .AsNoTracking()
            .FirstAsync();

        var beforeState = _dbContext.Cards.Update(card).State;

        var triggerContext = new Mock<ITriggerContext<Card>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Modified);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(card);

        triggerContext
            .SetupGet(t => t.UnmodifiedEntity)
            .Returns(null as Card);

        Task SaveAsync() => _immutableCard.BeforeSave(triggerContext.Object, default);

        await Assert.ThrowsAsync<DbUpdateException>(SaveAsync);
    }
}