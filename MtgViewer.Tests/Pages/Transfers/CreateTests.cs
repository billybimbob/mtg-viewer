using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MtgViewer.Data;
using MtgViewer.Pages.Transfers;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Pages.Transfers;

public class CreateTests : IAsyncLifetime
{
    private readonly CreateModel _createModel;
    private readonly CardDbContext _dbContext;
    private readonly ActionHandlerFactory _pageFactory;

    private readonly TestDataGenerator _testGen;
    private Deck _requestDeck = default!;

    public CreateTests(
        CreateModel createModel,
        CardDbContext dbContext,
        ActionHandlerFactory pageFactory,
        TestDataGenerator testGen)
    {
        _createModel = createModel;
        _dbContext = dbContext;
        _pageFactory = pageFactory;
        _testGen = testGen;
    }

    public async Task InitializeAsync()
    {
        await _testGen.SeedAsync();
        _requestDeck = await _testGen.CreateRequestDeckAsync();
    }

    public Task DisposeAsync() => _testGen.ClearAsync();

    private IQueryable<Trade> AllTrades =>
        _dbContext.Trades
            .AsNoTracking()
            .OrderBy(t => t.Id);

    [Fact]
    public async Task OnPost_WrongUser_NoChange()
    {
        // Arrange
        var wrongUser = await _dbContext.Owners.FirstAsync(o => o.Id != _requestDeck.OwnerId);
        await _pageFactory.AddPageContextAsync(_createModel, wrongUser.Id);

        var allTradeIds = AllTrades.Select(t => t.Id);

        // Act
        var tradesBefore = await allTradeIds.ToListAsync();
        var result = await _createModel.OnPostAsync(_requestDeck.Id, default);
        var tradesAfter = await allTradeIds.ToListAsync();

        // // Assert
        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(tradesBefore, tradesAfter);
    }

    [Fact]
    public async Task OnPost_InvalidDeck_NoChange()
    {
        // Arrange
        await _pageFactory.AddPageContextAsync(_createModel, _requestDeck.OwnerId);

        var allTradeIds = AllTrades.Select(t => t.Id);
        var wrongDeck = await _dbContext.Decks
            .AsNoTracking()
            .FirstAsync(d => d.OwnerId != _requestDeck.OwnerId);

        // Act
        var tradesBefore = await allTradeIds.ToListAsync();
        var result = await _createModel.OnPostAsync(wrongDeck.Id, default);
        var tradesAfter = await allTradeIds.ToListAsync();

        // // Assert
        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(tradesBefore, tradesAfter);
    }

    [Fact]
    public async Task OnPost_ValidDeck_Requests()
    {
        // Arrange
        await _pageFactory.AddPageContextAsync(_createModel, _requestDeck.OwnerId);

        // Act
        var tradesBefore = await AllTrades.Select(t => t.Id).ToListAsync();

        var result = await _createModel.OnPostAsync(_requestDeck.Id, default);
        var tradesAfter = await AllTrades.ToListAsync();

        var addedTrades = tradesAfter.ExceptBy(tradesBefore, t => t.Id);

        // // Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.NotEmpty(addedTrades);

        Assert.All(addedTrades, t =>
            Assert.Equal(_requestDeck.Id, t.ToId));
    }

    [Fact]
    public async Task OnPost_MultipleSources_RequestsAll()
    {
        // Arrange
        await _pageFactory.AddPageContextAsync(_createModel, _requestDeck.OwnerId);

        var requestCard = await _dbContext.Wants
            .Where(w => w.LocationId == _requestDeck.Id)
            .Select(w => w.Card)
            .AsNoTracking()
            .FirstAsync();

        var nonOwner = await _dbContext.Owners
            .FirstAsync(o => o.Id != _requestDeck.OwnerId);

        var extraLocations = Enumerable
            .Range(0, 3)
            .Select(i => new Deck
            {
                Name = $"Extra Deck #{i}",
                Owner = nonOwner
            })
            .ToList();

        var holds = extraLocations
            .Select(loc => new Hold
            {
                Card = requestCard,
                Location = loc,
                Copies = 2
            })
            .ToList();

        _dbContext.Decks.AttachRange(extraLocations);
        _dbContext.Holds.AttachRange(holds);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Act
        var tradesBefore = await AllTrades.Select(t => t.Id).ToListAsync();

        var result = await _createModel.OnPostAsync(_requestDeck.Id, default);
        var tradesAfter = await AllTrades.ToListAsync();

        var addedTrades = tradesAfter.ExceptBy(tradesBefore, t => t.Id);
        var addedTargets = addedTrades.Select(t => t.FromId);

        // // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.All(addedTrades, t =>
            Assert.Equal(_requestDeck.Id, t.ToId));

        Assert.All(extraLocations, l =>
            Assert.Contains(l.Id, addedTargets));
    }
}
