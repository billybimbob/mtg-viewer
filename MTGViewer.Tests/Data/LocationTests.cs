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

            var location = new Location("No owner location");

            dbContext.Attach(location);

            Assert.True(location.IsShared);
        }


        [Fact]
        public async Task IsShared_Owner_ReturnsFalse()
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

            Assert.False(location.IsShared);
        }


        [Fact]
        public async Task IsSharedFilter_ComputedProperty_ReturnsLocation()
        {
            await using var dbContext = TestHelpers.CardDbContext();
            await dbContext.SeedAsync();

            var sharedLocation = await dbContext.Locations.FirstAsync(l => l.IsShared);

            Assert.True(sharedLocation.IsShared);
        }
    }
}