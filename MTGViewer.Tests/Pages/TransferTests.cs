using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages
{
    public class TransferTests
    {
        [Fact]
        public async Task IndexOnPost_ValidSuggestion_RemovesSuggestion()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();

            var suggestQuery = dbContext.Suggestions.AsNoTracking();

            var suggestion = await suggestQuery
                .Include(s => s.Receiver)
                .FirstAsync();

            var indexModel = new IndexModel(userManager, dbContext);
            await indexModel.SetModelContextAsync(userManager, suggestion.Receiver);

            // Act
            var result = await indexModel.OnPostAsync(suggestion.Id);
            var suggestions = await suggestQuery.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.DoesNotContain(suggestion.Id, suggestions);
        }


        [Fact]
        public async Task IndexOnPost_WrongUser_NoRemove()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();

            var suggestQuery = dbContext.Suggestions.AsNoTracking();

            var suggestion = await suggestQuery
                .Include(s => s.Proposer)
                .FirstAsync();

            var indexModel = new IndexModel(userManager, dbContext);
            await indexModel.SetModelContextAsync(userManager, suggestion.Proposer);

            // Act
            var result = await indexModel.OnPostAsync(suggestion.Id);
            var suggestions = await suggestQuery.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Contains(suggestion.Id, suggestions);
        }


        [Fact]
        public async Task IndexOnPost_InvalidSuggestion_NoRemove()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();

            var tradeQuery = dbContext.Trades.AsNoTracking();

            var nonSuggestion = await tradeQuery
                .Include(t => t.Receiver)
                .FirstAsync();

            var indexModel = new IndexModel(userManager, dbContext);
            await indexModel.SetModelContextAsync(userManager, nonSuggestion.Receiver);

            // Act
            var result = await indexModel.OnPostAsync(nonSuggestion.Id);
            var suggestions = await tradeQuery.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Contains(nonSuggestion.Id, suggestions);
        }


        [Fact]
        public async Task StatusOnPost_ValidTrade_RemovesTrade()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();
            var (proposer, receiver, fromDeck) = await dbContext.GenerateTradeAsync();

            var statusModel = new StatusModel(userManager, dbContext);
            await statusModel.SetModelContextAsync(userManager, proposer);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.FromId == fromDeck.Id);

            var requestsQuery = dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
                .AsNoTracking()
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


        [Fact]
        public async Task StatusOnPost_WrongUser_NoRemove()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();
            var (proposer, receiver, fromDeck) = await dbContext.GenerateTradeAsync();

            var statusModel = new StatusModel(userManager, dbContext);
            await statusModel.SetModelContextAsync(userManager, receiver);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.FromId == fromDeck.Id);

            var requestsQuery = dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
                .AsNoTracking()
                .Select(t => t.Id);

            // Act
            var requestsBefore = await requestsQuery.ToListAsync();
            var result = await statusModel.OnPostAsync(trade.ToId);
            var requestsAfter = await requestsQuery.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(requestsBefore, requestsAfter);
        }


        [Fact]
        public async Task StatusOnPost_InvalidTrade_NoRemove()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();

            var (proposer, receiver, fromDeck) = await dbContext.GenerateTradeAsync();
            
            var statusModel = new StatusModel(userManager, dbContext);
            await statusModel.SetModelContextAsync(userManager, proposer);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.FromId == fromDeck.Id);

            var requestsQuery = dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
                .AsNoTracking()
                .Select(t => t.Id);

            // Act
            var requestsBefore = await requestsQuery.ToListAsync();
            var result = await statusModel.OnPostAsync(fromDeck.Id);
            var requestsAfter = await requestsQuery.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(requestsBefore, requestsAfter);
        }



        [Fact]
        public async Task ReviewOnPostAccept_ValidTrade_Applied()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();
            var (proposer, receiver, fromDeck) = await dbContext.GenerateTradeAsync();

            var reviewModel = new ReviewModel(dbContext, userManager);
            await reviewModel.SetModelContextAsync(userManager, receiver);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.FromId == fromDeck.Id);

            var tradeSourceQuery = dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == fromDeck.Id)
                .AsNoTracking();

            // Act
            var fromBefore = await tradeSourceQuery.SingleAsync();
            var result = await reviewModel.OnPostAcceptAsync(fromDeck.Id, trade.Id);
            var fromAfter = await tradeSourceQuery.SingleOrDefaultAsync();

            var tradeAfter = await dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
                .AsNoTracking()
                .Select(t => t.Id)
                .ToListAsync();

            var changeCheck = fromAfter is null
                || fromBefore.Amount > fromAfter.Amount && fromAfter.Amount >= 0;

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.DoesNotContain(trade.Id, tradeAfter);
            Assert.True(changeCheck);
        }


        [Fact]
        public async Task ReviewOnPostReject_ValidTrade_Applied()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();
            var (proposer, receiver, fromDeck) = await dbContext.GenerateTradeAsync();

            var reviewModel = new ReviewModel(dbContext, userManager);
            await reviewModel.SetModelContextAsync(userManager, receiver);

            var trade = await dbContext.Trades
                .AsNoTracking()
                .FirstAsync(t => t.ProposerId == proposer.Id && t.FromId == fromDeck.Id);

            var tradeSourceQuery = dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == trade.CardId
                    && ca.LocationId == fromDeck.Id)
                .AsNoTracking();

            // Act
            var fromBefore = await tradeSourceQuery.SingleAsync();
            var result = await reviewModel.OnPostRejectAsync(fromDeck.Id, trade.Id);
            var fromAfter = await tradeSourceQuery.SingleOrDefaultAsync();

            var tradesAfter = await dbContext.Trades
                .Where(t => t.ProposerId == proposer.Id && t.ToId == trade.ToId)
                .AsNoTracking()
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