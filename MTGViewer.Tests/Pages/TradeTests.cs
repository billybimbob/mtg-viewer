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
    public class TradeTests
    {

        [Fact]
        public async Task IndexOnPostAck_ValidSuggestion_RemovesSuggestion()
        {
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);

            using var userManager = TestHelpers.CardUserManager(services);
            var claimsFactory = TestHelpers.CardClaimsFactory(userManager);

            await dbContext.SeedAsync();

            var suggestion = await dbContext.Suggestions
                .Include(t => t.Receiver)
                .AsNoTracking()
                .FirstAsync();

            var userClaim = await claimsFactory.CreateAsync(suggestion.Receiver);
            var indexModel = new IndexModel(userManager, dbContext);

            indexModel.SetModelContext(userClaim);

            var result = await indexModel.OnPostAckAsync(suggestion.Id);
            var trades = await dbContext.Trades
                .AsNoTracking()
                .ToListAsync();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.DoesNotContain(suggestion.Id, trades.Select(t => t.Id));
        }


        [Fact]
        public async Task IndexOnPostAck_InvalidSuggestion_NoRemove()
        {
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);

            using var userManager = TestHelpers.CardUserManager(services);
            var claimsFactory = TestHelpers.CardClaimsFactory(userManager);

            await dbContext.SeedAsync();

            var nonSuggestion = await dbContext.Trades
                .Include(t => t.Receiver)
                .AsNoTracking()
                .FirstAsync(t => t.FromId != default);

            var userClaim = await claimsFactory.CreateAsync(nonSuggestion.Receiver);
            var indexModel = new IndexModel(userManager, dbContext);

            indexModel.SetModelContext(userClaim);

            var result = await indexModel.OnPostAckAsync(nonSuggestion.Id);
            var trades = await dbContext.Trades
                .AsNoTracking()
                .ToListAsync();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.Contains(nonSuggestion.Id, trades.Select(t => t.Id));
        }


        [Fact]
        public async Task ReviewOnPostAccept_ValidTrade_Applied()
        {
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);

            using var userManager = TestHelpers.CardUserManager(services);
            var claimsFactory = TestHelpers.CardClaimsFactory(userManager);

            await dbContext.SeedAsync();

            var (proposer, receiver, deck) = await dbContext.GenerateTradeAsync();

            var userClaim = await claimsFactory.CreateAsync(receiver);
            var reviewModel = new ReviewModel(dbContext, userManager);

            reviewModel.SetModelContext(userClaim);

            var tradeQuery = dbContext.Trades
                .Where(TradeFilter.Involves(proposer.Id, deck.Id))
                .AsNoTracking();

            var nonRequestAmounts = dbContext.Amounts
                .Where(ca => !ca.IsRequest);

            var tradeSourceQuery = tradeQuery
                .Join(nonRequestAmounts,
                    trade =>
                        new { trade.CardId, DeckId = trade.FromId },
                    amount =>
                        new { amount.CardId, DeckId = amount.LocationId },
                    (_, amount) => amount);

            var fromBefore = await tradeSourceQuery.ToListAsync();

            var result = await reviewModel.OnPostAcceptAsync(proposer.Id, deck.Id);

            var tradeAfter = await tradeQuery.ToListAsync();
            var fromAfter = await tradeSourceQuery.ToListAsync();

            var fromChanges = fromBefore
                .GroupJoin(fromAfter,
                    before => before.Id,
                    after => after.Id,
                    (before, afters) =>
                        (before, after: afters.FirstOrDefault()))
                .ToList();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.Empty(tradeAfter);

            Assert.True(fromAfter.All(ca => ca.Amount >= 0));

            Assert.True(fromChanges.All(fs => 
                fs.after is null
                    || fs.before.Amount > fs.after.Amount));
        }


        [Fact]
        public async Task ReviewOnPostReject_ValidTrade_Applied()
        {
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);

            using var userManager = TestHelpers.CardUserManager(services);
            var claimsFactory = TestHelpers.CardClaimsFactory(userManager);

            await dbContext.SeedAsync();

            var (proposer, receiver, deck) = await dbContext.GenerateTradeAsync();

            var userClaim = await claimsFactory.CreateAsync(receiver);
            var reviewModel = new ReviewModel(dbContext, userManager);

            reviewModel.SetModelContext(userClaim);

            var result = await reviewModel.OnPostRejectAsync(proposer.Id, deck.Id);

            var tradeExists = await dbContext.Trades
                .Where(TradeFilter.Involves(proposer.Id, deck.Id))
                .AnyAsync();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.False(tradeExists);
        }
    }
}