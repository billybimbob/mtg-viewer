using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MTGViewer.Data;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Data
{
    public class LocationTests
    {
        [Fact]
        public void IsShared_NoOwner_ReturnsTrue()
        {
            using var dbContext = TestHelpers.CardDbContext();

            var location = new Shared("No owner location");

            dbContext.Attach(location);

            Assert.True(location.Type == Discriminator.Shared);
        }


        [Fact]
        public async Task Type_Deck_IsDeckDiscriminator()
        {
            await using var services = TestHelpers.ServiceProvider();
            await using var dbContext = TestHelpers.CardDbContext(services);
            using var userManager = TestHelpers.CardUserManager(services);

            await dbContext.SeedAsync();

            var testUser = await userManager.Users.FirstAsync();

            var location = new Deck("Owned location")
            {
                Owner = testUser
            };

            dbContext.Attach(location);

            Assert.True(location.Type == Discriminator.Deck);
        }


        [Fact]
        public async Task Type_Locations_IsCorrectType()
        {
            await using var dbContext = TestHelpers.CardDbContext();
            await dbContext.SeedAsync();

            var shared = await dbContext.Locations.FirstAsync(l => l.Type == Discriminator.Shared);
            var deck = await dbContext.Locations.FirstAsync(l => l.Type == Discriminator.Deck);

            Assert.True(shared is Shared);
            Assert.True(deck is Deck);
        }
    }
}