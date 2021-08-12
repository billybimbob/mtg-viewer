using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MTGViewer.Data;
using MTGViewer.Pages.Trades;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Data
{
    public class TradeTests
    {
        [Fact]
        public async Task IsSuggestion_NoFrom_ReturnsTrue()
        {
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();

            var toLoc = await dbContext.Locations
                .Include(l => l.Owner)
                .FirstAsync(l => l.OwnerId != default);

            var proposer = await userManager.Users
                .FirstAsync(u => u.Id != toLoc.OwnerId);

            var card = await dbContext.Cards.FirstAsync();

            var trade = new Trade
            {
                Card = card,
                Proposer = proposer,
                Receiver = toLoc.Owner,
                To = toLoc,
                From = null
            };

            dbContext.Trades.Attach(trade);

            Assert.True(trade.IsSuggestion);
        }


        [Fact]
        public async Task IsSuggestion_WithFrom_ReturnsFalse()
        {
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();

            var fromLoc = await dbContext.Amounts
                .Include(ca => ca.Card)
                .Include(ca => ca.Location)
                    .ThenInclude(l => l.Owner)
                .FirstAsync(ca => ca.IsRequest == false);

            var toLoc = await dbContext.Locations
                .Include(l => l.Owner)
                .FirstAsync(l => l.OwnerId != default && l.Id != fromLoc.LocationId);

            var trade = new Trade
            {
                Card = fromLoc.Card,
                Proposer = fromLoc.Location.Owner,
                Receiver = toLoc.Owner,
                To = toLoc,
                From = fromLoc,
                Amount = 3
            };

            dbContext.Trades.Attach(trade);

            Assert.False(trade.IsSuggestion);
        }



        [Fact]
        public async Task IndexOnPost_ValidSuggestion_RemovesSuggestion()
        {
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);

            using var userManager = TestHelpers.CardUserManager(services);
            var claimsFactory = TestHelpers.CardClaimsFactory(userManager);

            await dbContext.SeedAsync();

            var suggestion = await dbContext.Trades
                .Include(t => t.Receiver)
                .AsNoTracking()
                .FirstAsync(t => t.FromId == default);

            var userClaim = await claimsFactory.CreateAsync(suggestion.Receiver);
            var indexModel = new IndexModel(userManager, dbContext);

            indexModel.SetModelContext(userClaim);

            var result = await indexModel.OnPostAsync(suggestion.Id);
            var trades = await dbContext.Trades
                .AsNoTracking()
                .ToListAsync();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.DoesNotContain(suggestion.Id, trades.Select(t => t.Id));
        }


        [Fact]
        public async Task IndexOnPost_InvalidSuggestion_NoRemove()
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

            var result = await indexModel.OnPostAsync(nonSuggestion.Id);
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

            var tradeBefore = await dbContext.Trades
                .Where(TradeFilter.Involves(proposer.Id, deck.Id))
                .Include(t => t.From)
                .AsNoTracking()
                .ToListAsync();

            var result = await reviewModel.OnPostAcceptAsync(proposer.Id, deck.Id);

            var tradeAfter = await dbContext.Trades
                .Where(TradeFilter.Involves(proposer.Id, deck.Id))
                .AsNoTracking()
                .ToListAsync();

            var fromAmounts = tradeBefore
                .Select(ca => ca.Id)
                .Distinct()
                .ToArray();

            var fromAfter = await dbContext.Amounts
                .Where(ca => fromAmounts.Contains(ca.Id))
                .AsNoTracking()
                .ToListAsync();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.False(tradeAfter.Any());
            Assert.True(fromAfter.All(ca => ca.Amount >= 0));
        }
    }
}