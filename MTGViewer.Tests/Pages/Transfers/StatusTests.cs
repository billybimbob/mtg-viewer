using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages.Transfers
{
    public class StatusTests
    {
        [Fact]
        public async Task OnPost_WrongUser_NoChange()
        {
            // Arrange
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var (proposer, receiver, toDeck) = await dbContext.GenerateRequestAsync();
            var statusModel = new StatusModel(userManager, dbContext);

            await statusModel.SetModelContextAsync(userManager, receiver.Id);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id);

            var requestsQuery = dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
                .Select(t => t.Id);

            // Act
            var requestsBefore = await requestsQuery.ToListAsync();
            var result = await statusModel.OnPostAsync(trade.ToId);

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
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var (proposer, receiver, toDeck) = await dbContext.GenerateRequestAsync();
            var statusModel = new StatusModel(userManager, dbContext);

            await statusModel.SetModelContextAsync(userManager, proposer.Id);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id);

            var requestsQuery = dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
                .Select(t => t.Id);

            // Act
            var requestsBefore = await requestsQuery.ToListAsync();
            var result = await statusModel.OnPostAsync(trade.FromId);

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
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var (proposer, receiver, toDeck) = await dbContext.GenerateRequestAsync();
            var statusModel = new StatusModel(userManager, dbContext);

            await statusModel.SetModelContextAsync(userManager, proposer.Id);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id);

            var requestsQuery = dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
                .Select(t => t.Id);

            // Act
            var requestsBefore = await requestsQuery.ToListAsync();
            var result = await statusModel.OnPostAsync(trade.ToId);

            var requestsAfter = await requestsQuery.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotEqual(requestsBefore, requestsAfter);
            Assert.DoesNotContain(trade.Id, requestsAfter);
        }
    }
}