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
        public void Type_Shared_IsCorrectDiscriminator()
        {
            using var dbContext = TestHelpers.CardDbContext();

            var location = new Shared("No owner location");

            dbContext.Attach(location);

            Assert.Equal(Discriminator.Shared, location.Type);
        }


        [Fact]
        public async Task Type_Deck_IsCorrectDiscriminator()
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

            Assert.Equal(Discriminator.Deck, location.Type);
        }


        [Fact]
        public async Task Discriminator_Locations_IsCorrectType()
        {
            await using var dbContext = TestHelpers.CardDbContext();
            await dbContext.SeedAsync();

            var shared = await dbContext.Locations.FirstAsync(l => l.Type == Discriminator.Shared);
            var deck = await dbContext.Locations.FirstAsync(l => l.Type == Discriminator.Deck);

            Assert.IsType<Shared>(shared);
            Assert.IsType<Deck>(deck);
        }
    }
}