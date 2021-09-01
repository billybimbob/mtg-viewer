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

            _reviewModel = new ReviewModel(_dbContext, _userManager);
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
            await _reviewModel.SetModelContextAsync(_userManager, _trades.ProposerId);

            var trade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId);

            var fromQuery = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId)
                .AsNoTracking();

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await _reviewModel.OnPostAcceptAsync(trade.Id);
            var fromAfter = await fromQuery.SingleAsync();

            var tradeAfter = await _dbContext.Trades
                .Where(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId)
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
            await _reviewModel.SetModelContextAsync(_userManager, _trades.ProposerId);

            var trade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId);

            var fromQuery = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId)
                .AsNoTracking();

            var wrongTrade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ToId != _trades.ToId);

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await _reviewModel.OnPostAcceptAsync(wrongTrade.Id);
            var fromAfter = await fromQuery.SingleAsync();

            var tradesAfter = await _dbContext.Trades
                .Where(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId)
                .Select(t => t.Id)
                .ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(fromBefore.Amount, fromAfter.Amount);
            Assert.Contains(trade.Id, tradesAfter);
        }


        [Fact]
        public async Task OnPostAccept_ValidTrade_AmountsChanged()
        {
            // Arrange
            var trade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId);

            await _reviewModel.SetModelContextAsync(_userManager, trade.ReceiverId);

            var tradeSourceQuery = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId)
                .AsNoTracking();

            // Act
            var fromBefore = await tradeSourceQuery.SingleAsync();
            var result = await _reviewModel.OnPostAcceptAsync(trade.Id);
            var fromAfter = await tradeSourceQuery.SingleOrDefaultAsync();

            var changeCheck = fromAfter is null
                || fromBefore.Amount > fromAfter.Amount && fromAfter.Amount >= 0;

            var tradeAfter = await _dbContext.Trades
                .Where(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId)
                .Select(t => t.Id)
                .ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.True(changeCheck);
            Assert.DoesNotContain(trade.Id, tradeAfter);
        }


        [Fact]
        public async Task OnPostAccept_LackAmount_CompletesOnlyTrade()
        {
            // Arrange
            var trade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId);

            await _reviewModel.SetModelContextAsync(_userManager, trade.ReceiverId);

            var fromAmount = await _dbContext.Amounts
                .SingleAsync(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId);

            fromAmount.Amount = 0;

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            var requestQuery = _dbContext.Trades
                .Where(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId)
                .Select(t => t.Id);

            // Act
            var requestBefore = await requestQuery.ToListAsync();
            var result = await _reviewModel.OnPostAcceptAsync(trade.Id);
            var requestAfter = await requestQuery.ToListAsync();

            var tradesRemoved = requestBefore.Except(requestAfter);

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Single(tradesRemoved);
            Assert.Contains(trade.Id, tradesRemoved);
        }


        [Fact]
        public async Task OnPostAccept_FullAmount_CompletesRequest()
        {
            // Arrange
            await _reviewModel.SetModelContextAsync(_userManager, _trades.ProposerId);

            var trade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId);

            var fromAmount = await _dbContext.Amounts
                .AsNoTracking()
                .SingleAsync(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId);

            var toRequest = await _dbContext.Amounts
                .SingleAsync(ca => ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == _trades.ToId);

            toRequest.Amount = fromAmount.Amount;

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            var requestQuery = _dbContext.Trades
                .Where(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId)
                .Select(t => t.Id);

            // Act
            var requestBefore = await requestQuery.ToListAsync();
            var result = await _reviewModel.OnPostAcceptAsync(trade.Id);
            var requestAfter = await requestQuery.ToListAsync();

            var tradesRemoved = requestBefore.Except(requestAfter);

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Contains(trade.Id, requestBefore);
            Assert.Empty(tradesRemoved);
        }



        [Fact]
        public async Task OnPostReject_WrongUser_NoChange()
        {
            // Arrange
            await _reviewModel.SetModelContextAsync(_userManager, _trades.ProposerId);

            var trade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId);

            var toQuery = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == _trades.ToId)
                .AsNoTracking();

            var fromQuery = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId)
                .AsNoTracking();

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await _reviewModel.OnPostRejectAsync(trade.Id);
            var fromAfter = await fromQuery.SingleOrDefaultAsync();

            var tradesAfter = await _dbContext.Trades
                .Where(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId)
                .Select(t => t.Id)
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
            await _reviewModel.SetModelContextAsync(_userManager, _trades.ProposerId);

            var trade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId);

            var toQuery = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == _trades.ToId)
                .AsNoTracking();

            var fromQuery = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId)
                .AsNoTracking();

            var wrongTrade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ToId != _trades.ToId);

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await _reviewModel.OnPostRejectAsync(wrongTrade.Id);
            var fromAfter = await fromQuery.SingleOrDefaultAsync();

            var tradesAfter = await _dbContext.Trades
                .Where(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId)
                .Select(t => t.Id)
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
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == _trades.Proposer.Id && t.ToId == _trades.ToId);

            await _reviewModel.SetModelContextAsync(_userManager, trade.ReceiverId);

            var toQuery = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == _trades.ToId)
                .AsNoTracking();

            var fromQuery = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId)
                .AsNoTracking();

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await _reviewModel.OnPostRejectAsync(trade.Id);
            var fromAfter = await fromQuery.SingleOrDefaultAsync();

            var tradesAfter = await _dbContext.Trades
                .Where(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId)
                .Select(t => t.Id)
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