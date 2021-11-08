using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages.Transfers
{
    public class ReviewTests : IAsyncLifetime
    {
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;
        private readonly TestDataGenerator _testGen;

        private readonly ReviewModel _reviewModel;
        private TradeSet _trades;

        public ReviewTests(
            CardDbContext dbContext,
            UserManager<CardUser> userManager,
            TestDataGenerator testGen)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _testGen = testGen;

            _reviewModel = new(_dbContext, _userManager);
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


        private IQueryable<Amount> ToTarget(Trade trade) =>
            _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == trade.ToId)
                .AsNoTracking();


        private IQueryable<Amount> FromTarget(Trade trade) =>
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
            Assert.Equal(fromBefore.NumCopies, fromAfter.NumCopies);
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
            Assert.Equal(fromBefore.NumCopies, fromAfter.NumCopies);
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

            var toAmount = ToTarget(trade).Select(ca => ca.NumCopies);
            var fromAmount = FromTarget(trade).Select(ca => ca.NumCopies);

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
            fromAmountTracked.NumCopies = 0;

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            var toAmount = ToTarget(trade).Select(ca => ca.NumCopies);
            var fromAmount = FromTarget(trade).Select(ca => ca.NumCopies);
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

            var fromAmount = FromTarget(trade).Select(ca => ca.NumCopies);

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

            var fromAmount = FromTarget(trade).Select(ca => ca.NumCopies);

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

            var fromAmount = FromTarget(trade).Select(ca => ca.NumCopies);

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