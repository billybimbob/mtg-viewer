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


        private IQueryable<Trade> TradesInSet => 
            _dbContext.Requests
                .Where(cr => !cr.IsReturn)
                .Join(_dbContext.Trades,
                    request => request.TargetId,
                    trade => trade.ToId,
                    (_, trade) => trade)
                .Distinct()
                .Where(t => t.ToId == _trades.TargetId)
                .AsNoTracking();


        [Fact]
        public async Task OnPost_WrongUser_NoChange()
        {
            // Arrange
            var trade = await TradesInSet.Include(t => t.From).FirstAsync();
            var tradeSet = TradesInSet.Select(t => t.Id);

            await _statusModel.SetModelContextAsync(_userManager, trade.From.OwnerId);

            // Act
            var tradesBefore = await tradeSet.ToListAsync();
            var result = await _statusModel.OnPostAsync(_trades.TargetId);
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
            await _statusModel.SetModelContextAsync(_userManager, _trades.Target.OwnerId);

            var trade = await TradesInSet.FirstAsync();
            var tradeSet = TradesInSet.Select(t => t.Id);

            // Act
            var tradesBefore = await tradeSet.ToListAsync();
            var result = await _statusModel.OnPostAsync(trade.FromId);
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
            await _statusModel.SetModelContextAsync(_userManager, _trades.Target.OwnerId);

            var tradeSet = TradesInSet.Select(t => t.Id);

            // Act
            var tradesBefore = await tradeSet.ToListAsync();
            var result = await _statusModel.OnPostAsync(_trades.TargetId);
            var tradesAfter = await tradeSet.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);

            Assert.NotEqual(tradesBefore, tradesAfter);
            Assert.All(tradesBefore, tId => Assert.DoesNotContain(tId, tradesAfter));
        }
    }
}