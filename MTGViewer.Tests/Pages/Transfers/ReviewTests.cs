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
            _trades = await _dbContext.CreateTradeSetAsync();
        }


        public async Task DisposeAsync()
        {
            await _services.DisposeAsync();
            await _dbContext.DisposeAsync();
            _userManager.Dispose();
        }


        [Fact]
        public async Task OnPostAccept_WrongUser_NoChange()
        {
            // Arrange
            await _reviewModel.SetModelContextAsync(_userManager, _trades.To.OwnerId);

            var trade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ToId == _trades.ToId);

            var fromQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == trade.FromId)
                .AsNoTracking();

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await _reviewModel.OnPostAcceptAsync(trade.Id, trade.Amount);
            var fromAfter = await fromQuery.SingleAsync();

            var tradeAfter = await _dbContext.Trades
                .Where(t => t.ToId == _trades.ToId)
                .Select(t => t.Id)
                .ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(fromBefore.Amount, fromAfter.Amount);
            Assert.Contains(trade.Id, tradeAfter);
        }


        [Fact]
        public async Task OnPostAccept_InvalidTrade_NoChange()
        {            
            // Arrange
            var trade = await _dbContext.Trades
                .Include(ex => ex.From)
                .AsNoTracking()
                .FirstAsync(t => t.ToId == _trades.ToId);

            await _reviewModel.SetModelContextAsync(_userManager, trade.From.OwnerId);

            var fromQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == trade.FromId)
                .AsNoTracking();

            var wrongTrade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ToId != _trades.ToId);

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await _reviewModel.OnPostAcceptAsync(wrongTrade.Id, trade.Amount);
            var fromAfter = await fromQuery.SingleAsync();

            var tradesAfter = await _dbContext.Trades
                .Where(t => t.ToId == _trades.ToId)
                .Select(ex => ex.Id)
                .ToListAsync();

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
            var trade = await _dbContext.Trades
                .Include(ex => ex.From)
                .AsNoTracking()
                .FirstAsync(t => t.ToId == _trades.ToId);

            if (amount <= 0)
            {
                amount = trade.Amount;
            }

            await _reviewModel.SetModelContextAsync(_userManager, trade.From.OwnerId);

            var toAmountQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == trade.ToId)
                .Select(ca => ca.Amount);

            var fromAmountQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == trade.FromId)
                .Select(ca => ca.Amount);

            // Act
            var toBefore = await toAmountQuery.SingleOrDefaultAsync();
            var fromBefore = await fromAmountQuery.SingleAsync();

            var result = await _reviewModel.OnPostAcceptAsync(trade.Id, amount);

            var toAfter = await toAmountQuery.SingleAsync();
            var fromAfter = await fromAmountQuery.SingleOrDefaultAsync();

            var tradeAfter = await _dbContext.Trades
                .Where(t => t.ToId == _trades.ToId)
                .Select(ex => ex.Id)
                .ToListAsync();

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
            var trade = await _dbContext.Trades
                .Include(ex => ex.From)
                .AsNoTracking()
                .FirstAsync(t => t.ToId == _trades.ToId);

            await _reviewModel.SetModelContextAsync(_userManager, trade.From.OwnerId);

            var fromAmount = await _dbContext.Amounts
                .SingleAsync(ca => ca.CardId == trade.CardId && ca.LocationId == trade.FromId);

            fromAmount.Amount = 0;

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            var toAmountQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == trade.ToId)
                .Select(ca => ca.Amount);

            var fromAmountQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == trade.FromId)
                .Select(ca => ca.Amount);

            var requestQuery = _dbContext.Trades
                .Where(t => t.ToId == _trades.ToId)
                .Select(ex => ex.Id);

            // Act
            var toBefore = await toAmountQuery.SingleOrDefaultAsync();
            var fromBefore = await fromAmountQuery.SingleAsync();
            var requestBefore = await requestQuery.ToListAsync();

            var result = await _reviewModel.OnPostAcceptAsync(trade.Id, trade.Amount);

            var toAfter = await toAmountQuery.SingleAsync();
            var fromAfter = await fromAmountQuery.SingleOrDefaultAsync();
            var requestAfter = await requestQuery.ToListAsync();

            var requestsFinished = requestBefore.Except(requestAfter);

            // Assert
            Assert.IsType<RedirectToPageResult>(result);

            Assert.Equal(toBefore, toAfter);
            Assert.Equal(fromBefore, fromAfter);

            Assert.Single(requestsFinished);
            Assert.Contains(trade.Id, requestsFinished);
        }


        [Fact]
        public async Task OnPostReject_WrongUser_NoChange()
        {
            // Arrange
            var trade = await _dbContext.Trades
                .Include(ex => ex.To)
                .AsNoTracking()
                .FirstAsync(ex => ex.ToId == _trades.ToId);

            await _reviewModel.SetModelContextAsync(_userManager, trade.To.OwnerId);

            var toQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == _trades.ToId)
                .AsNoTracking();

            var fromQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == trade.FromId)
                .AsNoTracking();

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await _reviewModel.OnPostRejectAsync(trade.Id);
            var fromAfter = await fromQuery.SingleOrDefaultAsync();

            var tradesAfter = await _dbContext.Trades
                .Where(t => t.ToId == _trades.ToId)
                .Select(ex => ex.Id)
                .ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(fromBefore.Amount, fromAfter.Amount);
            Assert.Contains(trade.Id, tradesAfter);
        }


        [Fact]
        public async Task OnPostReject_InvalidTrade_NoChange()
        {
            // Arrange
            var trade = await _dbContext.Trades
                .Include(ex => ex.From)
                .AsNoTracking()
                .FirstAsync(t => t.ToId == _trades.ToId);

            await _reviewModel.SetModelContextAsync(_userManager, trade.From.OwnerId);

            var toQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == _trades.ToId)
                .AsNoTracking();

            var fromQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == trade.FromId)
                .AsNoTracking();

            var wrongTrade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ToId != _trades.ToId);

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await _reviewModel.OnPostRejectAsync(wrongTrade.Id);
            var fromAfter = await fromQuery.SingleOrDefaultAsync();

            var tradesAfter = await _dbContext.Trades
                .Where(t => t.ToId == _trades.ToId)
                .Select(ex => ex.Id)
                .ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(fromBefore.Amount, fromAfter.Amount);
            Assert.Contains(trade.Id, tradesAfter);
        }


        [Fact]
        public async Task OnPostReject_ValidTrade_RemovesTrade()
        {
            // Arrange
            var trade = await _dbContext.Trades
                .Include(ex => ex.From)
                .AsNoTracking()
                .FirstAsync(t => t.ToId == _trades.ToId);

            await _reviewModel.SetModelContextAsync(_userManager, trade.From.OwnerId);

            var toQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == _trades.ToId)
                .AsNoTracking();

            var fromQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == trade.CardId && ca.LocationId == trade.FromId)
                .AsNoTracking();

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await _reviewModel.OnPostRejectAsync(trade.Id);
            var fromAfter = await fromQuery.SingleOrDefaultAsync();

            var tradesAfter = await _dbContext.Trades
                .Where(t => t.ToId == _trades.ToId)
                .Select(ex => ex.Id)
                .ToListAsync();
            
            var changeCheck = fromAfter is not null
                && fromBefore.Amount == fromAfter.Amount
                && fromAfter.Amount >= 0;

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.DoesNotContain(trade.Id, tradesAfter);
            Assert.True(changeCheck);
        }
    }
}