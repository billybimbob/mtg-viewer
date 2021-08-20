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
        public async Task IndexOnPostAck_ValidSuggestion_RemovesSuggestion()
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
            var result = await indexModel.OnPostAckAsync(suggestion.Id);
            var suggestions = await suggestQuery.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.DoesNotContain(suggestion.Id, suggestions.Select(t => t.Id));
        }


        [Fact]
        public async Task IndexOnPostAck_WrongUser_NoRemove()
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
            var result = await indexModel.OnPostAckAsync(suggestion.Id);
            var suggestions = await suggestQuery.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Contains(suggestion.Id, suggestions.Select(t => t.Id));
        }


        [Fact]
        public async Task IndexOnPostAck_InvalidSuggestion_NoRemove()
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
            var result = await indexModel.OnPostAckAsync(nonSuggestion.Id);
            var suggestions = await tradeQuery.ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Contains(nonSuggestion.Id, suggestions.Select(t => t.Id));
        }


        [Fact]
        public async Task IndexOnPostCancel_ValidTrade_RemovesTrade()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();
            var (proposer, receiver, deck) = await dbContext.GenerateTradeAsync();

            var indexModel = new IndexModel(userManager, dbContext);
            await indexModel.SetModelContextAsync(userManager, proposer);

            var involveQuery = dbContext.Trades
                .Where(TradeFilter.Involves(proposer.Id, deck.Id));

            // Act
            var tradeExistsBefore = await involveQuery.AnyAsync();
            var result = await indexModel.OnPostCancelAsync(proposer.Id, deck.Id);
            var tradeExistsAfter = await involveQuery.AnyAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.True(tradeExistsBefore);
            Assert.False(tradeExistsAfter);
        }


        [Fact]
        public async Task IndexOnPostCancel_WrongUser_NoRemove()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();
            var (proposer, receiver, deck) = await dbContext.GenerateTradeAsync();

            var indexModel = new IndexModel(userManager, dbContext);
            await indexModel.SetModelContextAsync(userManager, receiver);

            var involveQuery = dbContext.Trades
                .Where(TradeFilter.Involves(proposer.Id, deck.Id));

            // Act
            var tradeExistsBefore = await involveQuery.AnyAsync();
            var result = await indexModel.OnPostCancelAsync(proposer.Id, deck.Id);
            var tradeExistsAfter = await involveQuery.AnyAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.True(tradeExistsBefore);
            Assert.True(tradeExistsAfter);
        }


        [Fact]
        public async Task IndexOnPostCancel_InvalidTrade_NoRemove()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();

            var (proposer, receiver, deck) = await dbContext.GenerateTradeAsync();
            
            var indexModel = new IndexModel(userManager, dbContext);
            await indexModel.SetModelContextAsync(userManager, proposer);

            var involveQuery = dbContext.Trades
                .Where(TradeFilter.Involves(proposer.Id, deck.Id));

            var wrongDeck = await dbContext.Decks
                .AsNoTracking()
                .FirstAsync(t => t.Id != deck.Id);

            // Act
            var tradeExistsBefore = await involveQuery.AnyAsync();
            var result = await indexModel.OnPostCancelAsync(proposer.Id, wrongDeck.Id);
            var tradeExistsAfter = await involveQuery.AnyAsync();

            // Assert
            Assert.IsType<NotFoundResult>(result);
            Assert.True(tradeExistsBefore);
            Assert.True(tradeExistsAfter);
        }



        [Fact]
        public async Task ReviewOnPostAccept_ValidTrade_Applied()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();
            var (proposer, receiver, deck) = await dbContext.GenerateTradeAsync();

            var reviewModel = new ReviewModel(dbContext, userManager);
            await reviewModel.SetModelContextAsync(userManager, receiver);

            var tradeQuery = dbContext.Trades
                .Where(TradeFilter.Involves(proposer.Id, deck.Id))
                .AsNoTracking();

            var tradeSourceQuery = tradeQuery
                .Join( dbContext.Amounts.Where(ca => !ca.IsRequest),
                    trade =>
                        new { trade.CardId, DeckId = trade.FromId },
                    amount =>
                        new { amount.CardId, DeckId = amount.LocationId },
                    (_, amount) => amount);

            // Act
            var fromBefore = await tradeSourceQuery.ToListAsync();

            var result = await reviewModel.OnPostAcceptAsync(proposer.Id, deck.Id);

            var tradeAfter = await tradeQuery.ToListAsync();
            var fromAfter = await tradeSourceQuery.ToListAsync();

            var fromChange = fromBefore
                .GroupJoin(fromAfter,
                    before => before.Id,
                    after => after.Id,
                    (before, afters) =>
                        (before, after: afters.FirstOrDefault()))
                .ToList();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Empty(tradeAfter);

            var fromAfterValid = fromAfter.All(ca =>
                ca.Amount >= 0);

            var fromChangeValid = fromChange.All(fs => 
                fs.after is null || fs.before.Amount > fs.after.Amount);

            Assert.True(fromAfterValid);
            Assert.True(fromChangeValid);
        }


        [Fact]
        public async Task ReviewOnPostReject_ValidTrade_Applied()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();
            var (proposer, receiver, deck) = await dbContext.GenerateTradeAsync();

            var reviewModel = new ReviewModel(dbContext, userManager);
            await reviewModel.SetModelContextAsync(userManager, receiver);

            var involveQuery = dbContext.Trades
                .Where(TradeFilter.Involves(proposer.Id, deck.Id));

            // Act
            var tradeExistsBefore = await involveQuery.AnyAsync();
            var result = await reviewModel.OnPostRejectAsync(proposer.Id, deck.Id);
            var tradeExistsAfter = await involveQuery.AnyAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.True(tradeExistsBefore);
            Assert.False(tradeExistsAfter);
        }
    }
}