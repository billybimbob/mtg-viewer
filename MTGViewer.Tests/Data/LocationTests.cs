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
        public async Task First_IsShared_CorrectType()
        {
            await using var dbContext = TestHelpers.CardDbContext();
            await dbContext.SeedAsync();

            var shared = await dbContext.Locations.FirstAsync(l => l is Shared);

            Assert.IsType<Shared>(shared);
        }


        [Fact]
        public async Task First_IsDeck_CorrectType()
        {
            await using var dbContext = TestHelpers.CardDbContext();
            await dbContext.SeedAsync();

            var deck = await dbContext.Locations.FirstAsync(l => l is Deck);

            Assert.IsType<Deck>(deck);
        }


        [Fact]
        public async Task First_MultipleQueries_SameReference()
        {
            await using var dbContext = TestHelpers.CardDbContext();
            await dbContext.SeedAsync();

            var deck1 = await dbContext.Locations.FirstAsync(l => l is Deck);
            var deck2 = await dbContext.Locations.FirstAsync(l => l is Deck);

            Assert.Same(deck1, deck2);
        }
    }
}