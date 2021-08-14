using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Data;
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
        public async Task SuggestionFilter_IsSuggestion()
        {
            await using var dbContext = TestHelpers.CardDbContext();
            await dbContext.SeedAsync();

            var suggestion = await dbContext.Suggestions.FirstAsync(s => s.IsSuggestion);
            var trade = await dbContext.Trades.FirstAsync();

            Assert.True(suggestion.IsSuggestion);
            Assert.False(trade.IsSuggestion);
        }


        [Fact]
        public async Task TradeFilter_IsNotSuggestion()
        {
            await using var dbContext = TestHelpers.CardDbContext();
            await dbContext.SeedAsync();

            var suggestion = await dbContext.Suggestions.FirstAsync(s => !s.IsSuggestion);
            var trade = await dbContext.Trades.FirstAsync();

            Assert.True(suggestion is Trade);
            Assert.False(suggestion.IsSuggestion);

            Assert.False(trade.IsSuggestion);
        }
    }
}