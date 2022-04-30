using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using EntityFrameworkCore.Triggered;
using Moq;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Triggers;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Triggers;

public class TradeValidateTests : IAsyncLifetime
{
    private readonly TradeValidate _tradeValidate;
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;

    public TradeValidateTests(
        TradeValidate tradeValidate,
        CardDbContext dbContext,
        TestDataGenerator testGen)
    {
        _tradeValidate = tradeValidate;
        _dbContext = dbContext;
        _testGen = testGen;
    }

    public async Task InitializeAsync() => await _testGen.SeedAsync();

    public async Task DisposeAsync() => await _testGen.ClearAsync();

    [Fact]
    public async Task BeforeSave_NewTrade_Returns()
    {
        var card = await _dbContext.Cards.FirstAsync();

        var targets = await _dbContext.Decks
            .Take(2)
            .ToListAsync();

        var newTrade = new Trade
        {
            Card = card,
            To = targets[0],
            From = targets[1]
        };

        _dbContext.Trades.Add(newTrade);

        var triggerContext = new Mock<ITriggerContext<Trade>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Added);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(newTrade);

        await _tradeValidate.BeforeSave(triggerContext.Object, default);

        Assert.NotEqual(newTrade.FromId, newTrade.ToId);
    }

    [Fact]
    public async Task BeforeSave_TradeNewDecks_Returns()
    {
        var card = await _dbContext.Cards.FirstAsync();

        var users = await _dbContext.Users
            .Take(2)
            .ToListAsync();

        var newTrade = new Trade
        {
            Card = card,
            To = new Deck { Name = "To Deck", Owner = users[0] },
            From = new Deck { Name = "From Deck", Owner = users[1] }
        };

        bool sameTargetIds = newTrade.To.Id == newTrade.From.Id;

        _dbContext.Trades.Add(newTrade);

        var triggerContext = new Mock<ITriggerContext<Trade>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Added);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(newTrade);

        await _tradeValidate.BeforeSave(triggerContext.Object, default);

        Assert.True(sameTargetIds);
    }

    [Fact]
    public async Task BeforeSave_TradeSameTargets_Throws()
    {
        var card = await _dbContext.Cards.FirstAsync();

        var target = await _dbContext.Decks.FirstAsync();

        var newTrade = new Trade
        {
            Card = card,
            To = target,
            From = target
        };

        _dbContext.Trades.Add(newTrade);

        var triggerContext = new Mock<ITriggerContext<Trade>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Added);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(newTrade);

        Task SaveAsync() => _tradeValidate.BeforeSave(triggerContext.Object, default);

        await Assert.ThrowsAsync<DbUpdateException>(SaveAsync);
    }
}
