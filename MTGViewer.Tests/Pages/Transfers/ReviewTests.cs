using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Transfers;


public class ReviewTests : IAsyncLifetime
{
    private readonly ReviewModel _reviewModel;
    private readonly CardDbContext _dbContext;
    private readonly PageContextFactory _pageFactory;

    private readonly TestDataGenerator _testGen;
    private TradeSet _trades = default!;

    public ReviewTests(
        ReviewModel reviewModel,
        CardDbContext dbContext,
        PageContextFactory pageFactory,
        TestDataGenerator testGen)
    {
        _reviewModel = reviewModel;
        _dbContext = dbContext;
        _pageFactory = pageFactory;
        _testGen = testGen;
    }


    public async Task InitializeAsync()
    {
        await _testGen.SeedAsync();
        _trades = await _testGen.CreateTradeSetAsync(isToSet: false);
    }

    public Task DisposeAsync() => _testGen.ClearAsync();


    private IQueryable<Trade> TradesInSet =>
        _dbContext.Trades
            .Join(_dbContext.Wants,
                trade => trade.ToId,
                request => request.LocationId,
                (trade, _) => trade)
            .Distinct()
            .Where(t => t.FromId == _trades.TargetId)
            .AsNoTracking();


    private IQueryable<Hold> ToTarget(Trade trade) =>
        _dbContext.Holds
            .Where(h => h.CardId == trade.CardId && h.LocationId == trade.ToId)
            .AsNoTracking();


    private IQueryable<Hold> FromTarget(Trade trade) =>
        _dbContext.Holds
            .Where(h => h.CardId == trade.CardId && h.LocationId == _trades.TargetId)
            .AsNoTracking();


    [Fact]
    public async Task OnPostAccept_WrongUser_NoChange()
    {
        // Arrange
        var trade = await TradesInSet.Include(t => t.To).FirstAsync();

        await _pageFactory.AddModelContextAsync(_reviewModel, trade.To.OwnerId);

        // Act
        var fromBefore = await FromTarget(trade).SingleAsync();
        var result = await _reviewModel.OnPostAcceptAsync(trade.Id, trade.Copies, default);

        var fromAfter = await FromTarget(trade).SingleAsync();
        var tradeAfter = await TradesInSet.Select(t => t.Id).ToListAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(fromBefore.Copies, fromAfter.Copies);
        Assert.Contains(trade.Id, tradeAfter);
    }


    [Fact]
    public async Task OnPostAccept_InvalidTrade_NoChange()
    {
        // Arrange
        await _pageFactory.AddModelContextAsync(_reviewModel, _trades.Target.OwnerId);

        var trade = await TradesInSet.FirstAsync();
        var wrongTradeId = 0;

        // Act
        var fromBefore = await FromTarget(trade).SingleAsync();
        var result = await _reviewModel.OnPostAcceptAsync(wrongTradeId, trade.Copies, default);

        var fromAfter = await FromTarget(trade).SingleAsync();
        var tradesAfter = await TradesInSet.Select(t => t.Id).ToListAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(fromBefore.Copies, fromAfter.Copies);
        Assert.Contains(trade.Id, tradesAfter);
    }


    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task OnPostAccept_ValidTrade_HoldsAndRequestsChanged(int copies)
    {
        // Arrange
        await _pageFactory.AddModelContextAsync(_reviewModel, _trades.Target.OwnerId);

        var trade = await TradesInSet.FirstAsync();

        if (copies <= 0)
        {
            copies = trade.Copies;
        }

        var toCopies = ToTarget(trade).Select(h => h.Copies);
        var fromCopies = FromTarget(trade).Select(h => h.Copies);

        // Act
        var toBefore = await toCopies.SingleOrDefaultAsync();
        var fromBefore = await fromCopies.SingleAsync();

        var result = await _reviewModel.OnPostAcceptAsync(trade.Id, copies, default);

        var toAfter = await toCopies.SingleAsync();
        var fromAfter = await fromCopies.SingleOrDefaultAsync();

        var tradeAfter = await TradesInSet.Select(t => t.Id).ToListAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(trade.Copies, toAfter - toBefore);
        Assert.Equal(trade.Copies, fromBefore - fromAfter);

        Assert.DoesNotContain(trade.Id, tradeAfter);
    }


    [Fact]
    public async Task OnPostAccept_LackCopies_OnlyRemovesTrade()
    {
        // Arrange
        await _pageFactory.AddModelContextAsync(_reviewModel, _trades.Target.OwnerId);

        var trade = await TradesInSet.FirstAsync();

        var fromHold = await FromTarget(trade).AsTracking().SingleAsync();
        fromHold.Copies = 0;

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var toCopies = ToTarget(trade).Select(h => h.Copies);
        var fromCopies = FromTarget(trade).Select(h => h.Copies);
        var tradeSet = TradesInSet.Select(t => t.Id);

        // Act
        var toBefore = await toCopies.SingleOrDefaultAsync();
        var fromBefore = await fromCopies.SingleAsync();
        var tradesBefore = await tradeSet.ToListAsync();

        var result = await _reviewModel.OnPostAcceptAsync(trade.Id, trade.Copies, default);

        var toAfter = await toCopies.SingleAsync();
        var fromAfter = await fromCopies.SingleOrDefaultAsync();
        var tradesAfter = await tradeSet.ToListAsync();

        var tradesFinished = tradesBefore.Except(tradesAfter);

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(toBefore, toAfter);
        Assert.Equal(fromBefore, fromAfter);

        Assert.Single(tradesFinished);
        Assert.Contains(trade.Id, tradesFinished);
    }


    [Fact]
    public async Task OnPostReject_WrongUser_NoChange()
    {
        // Arrange
        var trade = await TradesInSet.Include(t => t.To).FirstAsync();

        await _pageFactory.AddModelContextAsync(_reviewModel, trade.To.OwnerId);

        var fromCopies = FromTarget(trade).Select(h => h.Copies);

        // Act
        var fromBefore = await fromCopies.SingleAsync();
        var result = await _reviewModel.OnPostRejectAsync(trade.Id, default);

        var fromAfter = await fromCopies.SingleOrDefaultAsync();
        var tradesAfter = await TradesInSet.Select(t => t.Id).ToListAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(fromBefore, fromAfter);
        Assert.Contains(trade.Id, tradesAfter);
    }


    [Fact]
    public async Task OnPostReject_InvalidTrade_NoChange()
    {
        // Arrange
        await _pageFactory.AddModelContextAsync(_reviewModel, _trades.Target.OwnerId);

        var trade = await TradesInSet.AsNoTracking().FirstAsync();
        var wrongTradeId = 0;

        var fromCopies = FromTarget(trade).Select(h => h.Copies);

        // Act
        var fromBefore = await fromCopies.SingleAsync();
        var result = await _reviewModel.OnPostRejectAsync(wrongTradeId, default);

        var fromAfter = await fromCopies.SingleOrDefaultAsync();
        var tradesAfter = await TradesInSet.Select(t => t.Id).ToListAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(fromBefore, fromAfter);
        Assert.Contains(trade.Id, tradesAfter);
    }


    [Fact]
    public async Task OnPostReject_ValidTrade_RemovesTrade()
    {
        // Arrange
        await _pageFactory.AddModelContextAsync(_reviewModel, _trades.Target.OwnerId);

        var trade = await TradesInSet.AsNoTracking().FirstAsync();

        var fromCopies = FromTarget(trade).Select(h => h.Copies);

        // Act
        var fromBefore = await fromCopies.SingleAsync();
        var result = await _reviewModel.OnPostRejectAsync(trade.Id, default);

        var fromAfter = await fromCopies.SingleAsync();
        var tradesAfter = await TradesInSet.Select(t => t.Id).ToListAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.DoesNotContain(trade.Id, tradesAfter);
        Assert.Equal(fromBefore, fromAfter);
    }
}