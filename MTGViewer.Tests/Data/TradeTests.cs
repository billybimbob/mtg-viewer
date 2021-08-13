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

            var toLoc = await dbContext.Decks
                .Include(l => l.Owner)
                .FirstAsync();

            var proposer = await userManager.Users
                .FirstAsync(u => u.Id != toLoc.OwnerId);

            var card = await dbContext.Cards.FirstAsync();

            var trade = new Suggestion
            {
                Card = card,
                Proposer = proposer,
                Receiver = toLoc.Owner,
                To = toLoc
            };

            dbContext.Suggestions.Attach(trade);

            Assert.True(trade.IsSuggestion);
        }


        [Fact]
        public async Task IsSuggestion_WithFrom_ReturnsFalse()
        {
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();

            var fromAmount = await dbContext.Amounts
                .Where(ca => !ca.Location.IsShared)
                .Include(ca => ca.Card)
                .Include(ca => ca.Location)
                .FirstAsync(ca => ca.IsRequest == false);

            var fromLoc = fromAmount.Location as Deck;

            var toLoc = await dbContext.Decks
                .Include(l => l.Owner)
                .FirstAsync(l => l.Id != fromAmount.LocationId);

            var trade = new Trade
            {
                Card = fromAmount.Card,
                Proposer = fromLoc.Owner,
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

            var suggestion = await dbContext.Suggestions
                .Include(t => t.Receiver)
                .AsNoTracking()
                .FirstAsync(s => s.IsSuggestion);

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

            var tradeQuery = dbContext.Trades
                .Where(TradeFilter.NotSuggestion)
                .Where(TradeFilter.Involves(proposer.Id, deck.Id));

            var nonRequestAmounts = dbContext.Amounts
                .Where(ca => !ca.IsRequest);

            var fromBefore = await tradeQuery
                .Join(nonRequestAmounts,
                    t => new { t.CardId, DeckId = t.FromId },
                    ca => new { ca.CardId, DeckId = ca.LocationId },
                    (t, ca) => ca)
                .AsNoTracking()
                .ToListAsync();

            var result = await reviewModel.OnPostAcceptAsync(proposer.Id, deck.Id);

            var tradeAfter = await tradeQuery
                .AsNoTracking()
                .ToListAsync();

            var fromAfter = await dbContext.Trades
                .Join(nonRequestAmounts,
                    t => new { t.CardId, DeckId = t.FromId },
                    ca => new { ca.CardId, DeckId = ca.LocationId },
                    (t, ca) => ca)
                .AsNoTracking()
                .ToListAsync();

            var fromChanges = fromBefore
                .GroupJoin(fromAfter,
                    before => before.Id,
                    after => after.Id,
                    (before, afters) =>
                        (before, after: afters.FirstOrDefault()))
                .ToList();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.False(tradeAfter.Any());

            Assert.True(fromAfter.All(ca => ca.Amount >= 0));
            Assert.True(fromChanges.All(fs => 
                fs.after is null || fs.before.Amount > fs.after.Amount));
        }
    }
}