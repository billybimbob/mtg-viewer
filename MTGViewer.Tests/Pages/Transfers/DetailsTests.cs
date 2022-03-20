using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Transfers;


public class StatusTests : IAsyncLifetime
{
    private readonly DetailsModel _detailsModel;
    private readonly CardDbContext _dbContext;
    private readonly PageContextFactory _pageFactory;

    private readonly TestDataGenerator _testGen;
    private TradeSet _trades = default!;

    public StatusTests(
        DetailsModel detailsModel,
        CardDbContext dbContext,
        PageContextFactory pageFactory,
        TestDataGenerator testGen)
    {
        _detailsModel = detailsModel;
        _dbContext = dbContext;
        _pageFactory = pageFactory;
        _testGen = testGen;
    }


    public async Task InitializeAsync()
    {
        await _testGen.SeedAsync();
        _trades = await _testGen.CreateTradeSetAsync(isToSet: true);
    }

    public Task DisposeAsync() => _testGen.ClearAsync();


    private IQueryable<Trade> TradesInSet =>
        _dbContext.Trades
            .Join(_dbContext.Wants,
                trade => trade.ToId,
                request => request.LocationId,
                (trade, _) => trade)
            .Distinct()
            .Where(t => t.ToId == _trades.TargetId)
            .AsNoTracking();


    [Fact]
    public async Task OnPost_WrongUser_NoChange()
    {
        // Arrange
        var trade = await TradesInSet.Include(t => t.From).FirstAsync();
        var tradeSet = TradesInSet.Select(t => t.Id);

        await _pageFactory.AddModelContextAsync(_detailsModel, trade.From.OwnerId);

        // Act
        var tradesBefore = await tradeSet.ToListAsync();
        var result = await _detailsModel.OnPostAsync(_trades.TargetId, default);
        var tradesAfter = await tradeSet.ToListAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(tradesBefore, tradesAfter);
        Assert.Contains(trade.Id, tradesAfter);
    }


    [Fact]
    public async Task OnPost_InvalidTrade_NoChange()
    {
        // Arrange
        await _pageFactory.AddModelContextAsync(_detailsModel, _trades.Target.OwnerId);

        var trade = await TradesInSet.FirstAsync();
        var tradeSet = TradesInSet.Select(t => t.Id);

        // Act
        var tradesBefore = await tradeSet.ToListAsync();
        var result = await _detailsModel.OnPostAsync(trade.FromId, default);
        var tradesAfter = await tradeSet.ToListAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(tradesBefore, tradesAfter);
        Assert.Contains(trade.Id, tradesAfter);
    }


    [Fact]
    public async Task OnPost_ValidTrade_RemovesTrade()
    {
        // Arrange
        await _pageFactory.AddModelContextAsync(_detailsModel, _trades.Target.OwnerId);

        var tradeSet = TradesInSet.Select(t => t.Id);

        // Act
        var tradesBefore = await tradeSet.ToListAsync();
        var result = await _detailsModel.OnPostAsync(_trades.TargetId, default);
        var tradesAfter = await tradeSet.ToListAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.NotEqual(tradesBefore, tradesAfter);

        Assert.All(tradesBefore, tId =>
            Assert.DoesNotContain(tId, tradesAfter));
    }
}