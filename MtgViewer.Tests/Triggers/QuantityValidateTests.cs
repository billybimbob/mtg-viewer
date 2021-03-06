using System.Linq;
using System.Threading.Tasks;

using EntityFrameworkCore.Triggered;
using Microsoft.EntityFrameworkCore;

using Moq;
using Xunit;

using MtgViewer.Data;
using MtgViewer.Services;
using MtgViewer.Tests.Utils;
using MtgViewer.Triggers;

namespace MtgViewer.Tests.Triggers;

public class QuantityValidateTests : IAsyncLifetime
{
    private readonly QuantityValidate _quantityValidate;
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;
    private readonly TestDataGenerator _testGen;

    public QuantityValidateTests(
        QuantityValidate quantityValidate,
        CardDbContext dbContext,
        PageSize pageSize,
        TestDataGenerator testGen)
    {
        _quantityValidate = quantityValidate;
        _dbContext = dbContext;
        _pageSize = pageSize;
        _testGen = testGen;
    }

    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();

    [Fact]
    public async Task BeforeSave_RemoveQuantity_Returns()
    {
        var quantity = await _dbContext.Holds.FirstAsync();

        _dbContext.Holds.Remove(quantity);

        var triggerContext = new Mock<ITriggerContext<Quantity>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Deleted);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(quantity);

        await _quantityValidate.BeforeSave(triggerContext.Object, default);

        var state = _dbContext.Entry(quantity).State;

        Assert.Equal(EntityState.Deleted, state);
    }

    [Fact]
    public async Task BeforeSave_AddValidQuantity_Returns()
    {
        var box = await _dbContext.Boxes.FirstAsync();

        var card = await _dbContext.Cards
            .FirstAsync(c => c.Holds.All(h => h.LocationId != box.Id));

        var quantity = new Hold
        {
            Card = card,
            Location = box,
            Copies = 3
        };

        _dbContext.Holds.Add(quantity);

        var triggerContext = new Mock<ITriggerContext<Quantity>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Added);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(quantity);

        await _quantityValidate.BeforeSave(triggerContext.Object, default);

        var state = _dbContext.Entry(quantity).State;

        Assert.Equal(EntityState.Added, state);
    }

    [Fact]
    public async Task BeforeSave_CopiesAboveLimit_CopiesLowered()
    {
        var quantity = await _dbContext.Holds.FirstAsync();

        quantity.Copies = _pageSize.Limit + 3;

        var triggerContext = new Mock<ITriggerContext<Quantity>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Modified);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(quantity);

        await _quantityValidate.BeforeSave(triggerContext.Object, default);

        Assert.Equal(_pageSize.Limit, quantity.Copies);
    }

    [Fact]
    public async Task BeforeSave_GivebackMissingLocation_Throws()
    {
        var hold = await _dbContext.Holds
            .Include(h => h.Card)
            .FirstAsync(h =>
                h.Card.Givebacks.All(g => g.LocationId != h.LocationId));

        var giveback = new Giveback
        {
            Card = hold.Card,
            LocationId = hold.LocationId,
            Copies = 3
        };

        _dbContext.Givebacks.Add(giveback);

        var triggerContext = new Mock<ITriggerContext<Quantity>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Added);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(giveback);

        Task SaveAsync() => _quantityValidate.BeforeSave(triggerContext.Object, default);

        await Assert.ThrowsAsync<DbUpdateException>(SaveAsync);
    }

    [Fact]
    public async Task BeforeSave_GivebackMissingHolds_Throws()
    {
        var deck = await _dbContext.Decks.FirstAsync();

        var card = await _dbContext.Cards
            .FirstAsync(c => c.Holds.All(h => h.LocationId != deck.Id));

        var giveback = new Giveback
        {
            Card = card,
            Location = deck,
            Copies = 3
        };

        _dbContext.Givebacks.Add(giveback);

        var triggerContext = new Mock<ITriggerContext<Quantity>>();

        triggerContext
            .SetupGet(t => t.ChangeType)
            .Returns(ChangeType.Added);

        triggerContext
            .SetupGet(t => t.Entity)
            .Returns(giveback);

        Task SaveAsync()
            => _quantityValidate.BeforeSave(triggerContext.Object, default);

        await Assert.ThrowsAsync<DbUpdateException>(SaveAsync);
    }
}
