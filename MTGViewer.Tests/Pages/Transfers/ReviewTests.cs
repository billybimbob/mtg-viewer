using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages.Transfers
{
    public class ReviewTests : IAsyncLifetime
    {
        private readonly ServiceProvider _services;
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        private readonly ReviewModel _reviewModel;
        private TradeSet _trades;

        public ReviewTests()
        {
            _services = TestFactory.ServiceProvider();
            _dbContext = TestFactory.CardDbContext(_services);
            _userManager = TestFactory.CardUserManager(_services);

            _reviewModel = new(_dbContext, _userManager);
        }


        public async Task InitializeAsync()
        {
            await _dbContext.SeedAsync(_userManager);
            _trades = await _dbContext.CreateTradeSetAsync(isToSet: false);
        }


        public async Task DisposeAsync()
        {
            await _services.DisposeAsync();
            await _dbContext.DisposeAsync();
            _userManager.Dispose();
        }


        private IQueryable<Trade> TradesInSet => 
            _dbContext.Requests
                .Where(cr => !cr.IsReturn)
                .Join(_dbContext.Trades,
                    request => request.TargetId,
                    trade => trade.ToId,
                    (_, trade) => trade)
                .Distinct()
                .Where(t => t.FromId == _trades.TargetId)
                .AsNoTracking();


        private IQueryable<CardAmount> ToTarget(Trade trade) =>
            _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == trade.ToId)
                .AsNoTracking();


        private IQueryable<CardAmount> FromTarget(Trade trade) =>
            _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == _trades.TargetId)
                .AsNoTracking();


        [Fact]
        public async Task OnPostAccept_WrongUser_NoChange()
        {
            // Arrange
            var trade = await TradesInSet.Include(t => t.To).FirstAsync();

            await _reviewModel.SetModelContextAsync(_userManager, trade.To.OwnerId);

            // Act
            var fromBefore = await FromTarget(trade).SingleAsync();
            var result = await _reviewModel.OnPostAcceptAsync(trade.Id, trade.Amount);

            var fromAfter = await FromTarget(trade).SingleAsync();
            var tradeAfter = await TradesInSet.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(fromBefore.Amount, fromAfter.Amount);
            Assert.Contains(trade.Id, tradeAfter);
        }


        [Fact]
        public async Task OnPostAccept_InvalidTrade_NoChange()
        {            
            // Arrange
            await _reviewModel.SetModelContextAsync(_userManager, _trades.Target.OwnerId);

            var trade = await TradesInSet.FirstAsync();
            var wrongTradeId = 0;

            // Act
            var fromBefore = await FromTarget(trade).SingleAsync();
            var result = await _reviewModel.OnPostAcceptAsync(wrongTradeId, trade.Amount);

            var fromAfter = await FromTarget(trade).SingleAsync();
            var tradesAfter = await TradesInSet.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(fromBefore.Amount, fromAfter.Amount);
            Assert.Contains(trade.Id, tradesAfter);
        }


        [Theory]
        [InlineData(-1)]
        [InlineData(1)]
        [InlineData(5)]
        public async Task OnPostAccept_ValidTrade_AmountsAndRequestsChanged(int amount)
        {
            // Arrange
            await _reviewModel.SetModelContextAsync(_userManager, _trades.Target.OwnerId);

            var trade = await TradesInSet.FirstAsync();

            if (amount <= 0)
            {
                amount = trade.Amount;
            }

            var toAmount = ToTarget(trade).Select(ca => ca.Amount);
            var fromAmount = FromTarget(trade).Select(ca => ca.Amount);

            // Act
            var toBefore = await toAmount.SingleOrDefaultAsync();
            var fromBefore = await fromAmount.SingleAsync();

            var result = await _reviewModel.OnPostAcceptAsync(trade.Id, amount);

            var toAfter = await toAmount.SingleAsync();
            var fromAfter = await fromAmount.SingleOrDefaultAsync();

            var tradeAfter = await TradesInSet.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);

            Assert.Equal(trade.Amount, toAfter - toBefore);
            Assert.Equal(trade.Amount, fromBefore - fromAfter);

            Assert.DoesNotContain(trade.Id, tradeAfter);
        }


        [Fact]
        public async Task OnPostAccept_LackAmount_OnlyRemovesTrade()
        {
            // Arrange
            await _reviewModel.SetModelContextAsync(_userManager, _trades.Target.OwnerId);

            var trade = await TradesInSet.FirstAsync();

            var fromAmountTracked = await FromTarget(trade).AsTracking().SingleAsync();
            fromAmountTracked.Amount = 0;

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            var toAmount = ToTarget(trade).Select(ca => ca.Amount);
            var fromAmount = FromTarget(trade).Select(ca => ca.Amount);
            var tradeSet = TradesInSet.Select(t => t.Id);

            // Act
            var toBefore = await toAmount.SingleOrDefaultAsync();
            var fromBefore = await fromAmount.SingleAsync();
            var tradesBefore = await tradeSet.ToListAsync();

            var result = await _reviewModel.OnPostAcceptAsync(trade.Id, trade.Amount);

            var toAfter = await toAmount.SingleAsync();
            var fromAfter = await fromAmount.SingleOrDefaultAsync();
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

            await _reviewModel.SetModelContextAsync(_userManager, trade.To.OwnerId);

            var fromAmount = FromTarget(trade).Select(ca => ca.Amount);

            // Act
            var fromBefore = await fromAmount.SingleAsync();
            var result = await _reviewModel.OnPostRejectAsync(trade.Id);

            var fromAfter = await fromAmount.SingleOrDefaultAsync();
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
            await _reviewModel.SetModelContextAsync(_userManager, _trades.Target.OwnerId);

            var trade = await TradesInSet.AsNoTracking().FirstAsync();
            var wrongTradeId = 0;

            var fromAmount = FromTarget(trade).Select(ca => ca.Amount);

            // Act
            var fromBefore = await fromAmount.SingleAsync();
            var result = await _reviewModel.OnPostRejectAsync(wrongTradeId);

            var fromAfter = await fromAmount.SingleOrDefaultAsync();
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
            await _reviewModel.SetModelContextAsync(_userManager, _trades.Target.OwnerId);

            var trade = await TradesInSet.AsNoTracking().FirstAsync();

            var fromAmount = FromTarget(trade).Select(ca => ca.Amount);

            // Act
            var fromBefore = await fromAmount.SingleAsync();
            var result = await _reviewModel.OnPostRejectAsync(trade.Id);

            var fromAfter = await fromAmount.SingleAsync();
            var tradesAfter = await TradesInSet.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);

            Assert.DoesNotContain(trade.Id, tradesAfter);
            Assert.Equal(fromBefore, fromAfter);
        }
    }
}