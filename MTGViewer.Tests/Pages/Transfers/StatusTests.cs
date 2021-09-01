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
    public class StatusTests : IAsyncLifetime
    {
        private readonly ServiceProvider _services;
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        private readonly StatusModel _statusModel;
        private TradeSet _trades;

        public StatusTests()
        {
            _services = TestFactory.ServiceProvider();
            _dbContext = TestFactory.CardDbContext(_services);
            _userManager = TestFactory.CardUserManager(_services);

            _statusModel = new StatusModel(_userManager, _dbContext);
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
        public async Task OnPost_WrongUser_NoChange()
        {
            // Arrange
            var trade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId);

            await _statusModel.SetModelContextAsync(_userManager, trade.ReceiverId);

            var requestsQuery = _dbContext.Trades
                .Where(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId)
                .Select(t => t.Id);

            // Act
            var requestsBefore = await requestsQuery.ToListAsync();
            var result = await _statusModel.OnPostAsync(_trades.ToId);
            var requestsAfter = await requestsQuery.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(requestsBefore, requestsAfter);
            Assert.Contains(trade.Id, requestsAfter);
        }


        [Fact]
        public async Task OnPost_InvalidTrade_NoChange()
        {
            // Arrange
            await _statusModel.SetModelContextAsync(_userManager, _trades.ProposerId);

            var trade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId);

            var requestsQuery = _dbContext.Trades
                .Where(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId)
                .Select(t => t.Id);

            // Act
            var requestsBefore = await requestsQuery.ToListAsync();
            var result = await _statusModel.OnPostAsync(trade.FromId);
            var requestsAfter = await requestsQuery.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(requestsBefore, requestsAfter);
            Assert.Contains(trade.Id, requestsAfter);
        }


        [Fact]
        public async Task OnPost_ValidTrade_RemovesTrade()
        {
            // Arrange
            await _statusModel.SetModelContextAsync(_userManager, _trades.ProposerId);

            var trade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId);

            var requestsQuery = _dbContext.Trades
                .Where(t => t.ProposerId == _trades.ProposerId && t.ToId == _trades.ToId)
                .Select(t => t.Id);

            // Act
            var requestsBefore = await requestsQuery.ToListAsync();
            var result = await _statusModel.OnPostAsync(_trades.ToId);
            var requestsAfter = await requestsQuery.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotEqual(requestsBefore, requestsAfter);
            Assert.DoesNotContain(trade.Id, requestsAfter);
        }
    }
}