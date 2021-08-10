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

            await SeedData.AddTo(userManager, dbContext);

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

            await SeedData.AddTo(userManager, dbContext);

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
    }
}