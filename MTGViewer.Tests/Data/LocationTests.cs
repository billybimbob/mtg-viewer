using System.Threading.Tasks;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Data
{
    public class LocationTests
    {
        [Fact]
        public async Task IsShared_NoOwner_ReturnsTrue()
        {
            await using var dbContext = new CardDbContext(CardDbUtils.TestCardDbOptions());

            var location = new Location("No owner location")
            {
                Owner = null
            };

            dbContext.Attach(location);
            bool isShared = location.IsShared;

            Assert.True(isShared);
        }
    }
}