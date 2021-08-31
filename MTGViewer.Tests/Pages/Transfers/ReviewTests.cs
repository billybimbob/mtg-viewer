using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages.Transfers
{
    public class ReviewTests
    {
        [Fact]
        public async Task OnPostAccept_WrongUser_NoChange()
        {
            // Arrange
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var (proposer, receiver, toDeck) = await dbContext.GenerateRequestAsync();
            var reviewModel = new ReviewModel(dbContext, userManager);

            await reviewModel.SetModelContextAsync(userManager, proposer.Id);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id);

            var fromQuery = dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId)
                .AsNoTracking();

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await reviewModel.OnPostAcceptAsync(trade.Id);
            var fromAfter = await fromQuery.SingleAsync();

            var tradeAfter = await dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
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
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var (proposer, receiver, toDeck) = await dbContext.GenerateRequestAsync();
            var reviewModel = new ReviewModel(dbContext, userManager);

            await reviewModel.SetModelContextAsync(userManager, proposer.Id);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id);

            var fromQuery = dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId)
                .AsNoTracking();

            var wrongTrade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ToId != toDeck.Id);

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await reviewModel.OnPostAcceptAsync(wrongTrade.Id);
            var fromAfter = await fromQuery.SingleAsync();

            var tradesAfter = await dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
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
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var (proposer, receiver, toDeck) = await dbContext.GenerateRequestAsync();
            var reviewModel = new ReviewModel(dbContext, userManager);

            await reviewModel.SetModelContextAsync(userManager, receiver.Id);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id);

            var tradeSourceQuery = dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId)
                .AsNoTracking();

            // Act
            var fromBefore = await tradeSourceQuery.SingleAsync();
            var result = await reviewModel.OnPostAcceptAsync(trade.Id);
            var fromAfter = await tradeSourceQuery.SingleOrDefaultAsync();

            var changeCheck = fromAfter is null
                || fromBefore.Amount > fromAfter.Amount && fromAfter.Amount >= 0;

            var tradeAfter = await dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
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
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var (proposer, receiver, toDeck) = await dbContext.GenerateRequestAsync();
            var reviewModel = new ReviewModel(dbContext, userManager);

            await reviewModel.SetModelContextAsync(userManager, receiver.Id);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id);

            var fromAmount = await dbContext.Amounts
                .SingleAsync(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId);

            fromAmount.Amount = 0;

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            var requestQuery = dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id)
                .Select(t => t.Id);

            // Act
            var requestBefore = await requestQuery.ToListAsync();
            var result = await reviewModel.OnPostAcceptAsync(trade.Id);

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
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var (proposer, receiver, toDeck) = await dbContext.GenerateRequestAsync();
            var reviewModel = new ReviewModel(dbContext, userManager);

            await reviewModel.SetModelContextAsync(userManager, proposer.Id);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id);

            var fromAmount = await dbContext.Amounts
                .AsNoTracking()
                .SingleAsync(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId);

            var toRequest = await dbContext.Amounts
                .SingleAsync(ca => ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.ToId);

            toRequest.Amount = fromAmount.Amount;

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            var requestQuery = dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id)
                .Select(t => t.Id);

            // Act
            var requestBefore = await requestQuery.ToListAsync();
            var result = await reviewModel.OnPostAcceptAsync(trade.Id);

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
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var (proposer, receiver, toDeck) = await dbContext.GenerateRequestAsync();
            var reviewModel = new ReviewModel(dbContext, userManager);

            await reviewModel.SetModelContextAsync(userManager, proposer.Id);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id);

            var toQuery = dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.ToId)
                .AsNoTracking();

            var fromQuery = dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId)
                .AsNoTracking();

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await reviewModel.OnPostRejectAsync(trade.Id);
            var fromAfter = await fromQuery.SingleOrDefaultAsync();

            var tradesAfter = await dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
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
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var (proposer, receiver, toDeck) = await dbContext.GenerateRequestAsync();
            var reviewModel = new ReviewModel(dbContext, userManager);

            await reviewModel.SetModelContextAsync(userManager, proposer.Id);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id);

            var toQuery = dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.ToId)
                .AsNoTracking();

            var fromQuery = dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId)
                .AsNoTracking();

            var wrongTrade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ToId != toDeck.Id);

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await reviewModel.OnPostRejectAsync(wrongTrade.Id);
            var fromAfter = await fromQuery.SingleOrDefaultAsync();

            var tradesAfter = await dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
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
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var (proposer, receiver, toDeck) = await dbContext.GenerateRequestAsync();
            var reviewModel = new ReviewModel(dbContext, userManager);

            await reviewModel.SetModelContextAsync(userManager, receiver.Id);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.ToId == toDeck.Id);

            var toQuery = dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.ToId)
                .AsNoTracking();

            var fromQuery = dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == trade.FromId)
                .AsNoTracking();

            // Act
            var fromBefore = await fromQuery.SingleAsync();
            var result = await reviewModel.OnPostRejectAsync(trade.Id);
            var fromAfter = await fromQuery.SingleOrDefaultAsync();

            var tradesAfter = await dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
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