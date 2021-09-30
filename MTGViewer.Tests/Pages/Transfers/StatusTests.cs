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

            _statusModel = new(_userManager, _dbContext);
        }


        public async Task InitializeAsync()
        {
            await _dbContext.SeedAsync(_userManager);
            _trades = await _dbContext.CreateTradeSetAsync(isToSet: true);
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
                .Include(t => t.From)
                .AsNoTracking()
                .FirstAsync(t => t.ToId == _trades.TargetId);

            await _statusModel.SetModelContextAsync(_userManager, trade.From.OwnerId);

            var tradesQuery = _dbContext.Trades
                .Where(t => t.ToId == _trades.TargetId)
                .Select(t => t.Id);

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await _statusModel.OnPostAsync(_trades.TargetId);
            var tradesAfter = await tradesQuery.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(tradesBefore, tradesAfter);
            Assert.Contains(trade.Id, tradesAfter);
        }


        [Fact]
        public async Task OnPost_InvalidTrade_NoChange()
        {
            // Arrange
            await _statusModel.SetModelContextAsync(_userManager, _trades.Target.OwnerId);

            var trade = await _dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ToId == _trades.TargetId);

            var tradesQuery = _dbContext.Trades
                .Where(t => t.ToId == _trades.TargetId)
                .Select(t => t.Id);

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await _statusModel.OnPostAsync(trade.FromId);
            var tradesAfter = await tradesQuery.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(tradesBefore, tradesAfter);
            Assert.Contains(trade.Id, tradesAfter);
        }


        [Fact]
        public async Task OnPost_ValidTrade_RemovesTrade()
        {
            // Arrange
            await _statusModel.SetModelContextAsync(_userManager, _trades.Target.OwnerId);

            var targetTrades = _trades.Select(t => t.Id);
            var tradesQuery = _dbContext.Trades.Select(t => t.Id);

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await _statusModel.OnPostAsync(_trades.TargetId);
            var tradesAfter = await tradesQuery.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);

            Assert.NotEqual(tradesBefore, tradesAfter);
            Assert.All(targetTrades, tId => Assert.DoesNotContain(tId, tradesAfter));
        }
    }
}