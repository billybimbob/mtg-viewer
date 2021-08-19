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
        public async Task Type_Suggestion_IsCorrectDiscriminator()
        {
            // Arrange
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

            var suggestion = new Suggestion
            {
                Card = card,
                Proposer = proposer,
                Receiver = toLoc.Owner,
                To = toLoc
            };

            // Act
            dbContext.Suggestions.Attach(suggestion);

            // Assert
            Assert.Equal(Discriminator.Suggestion, suggestion.Type);
        }


        [Fact]
        public async Task Type_Trade_IsCorrectDiscriminator()
        {
            // Arrange
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();

            var fromAmount = await dbContext.Amounts
                .Where(ca => ca.Location.Type == Discriminator.Deck)
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

            // Act
            dbContext.Trades.Attach(trade);

            // Assert
            Assert.Equal(Discriminator.Trade, trade.Type);
        }


        [Fact]
        public async Task Discriminator_Suggestion_IsCorrectType()
        {
            await using var dbContext = TestHelpers.CardDbContext();
            await dbContext.SeedAsync();

            var suggestion = await dbContext.Transfers.FirstAsync(t => t.Type == Discriminator.Suggestion);

            Assert.IsType<Suggestion>(suggestion);
        }


        [Fact]
        public async Task Discriminator_Trade_IsCorrectType()
        {
            await using var dbContext = TestHelpers.CardDbContext();
            await dbContext.SeedAsync();

            var trade = await dbContext.Transfers.FirstAsync(t => t.Type == Discriminator.Trade);

            Assert.IsType<Trade>(trade);
        }
    }
}