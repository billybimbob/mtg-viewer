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

            var location = new Location("No owner location")
            {
                Owner = null
            };

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

            var location = new Location("Owned location")
            {
                Owner = testUser
            };

            dbContext.Attach(location);

            Assert.False(location.IsShared);
        }
    }
}