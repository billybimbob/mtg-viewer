using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MTGViewer.Data;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Data
{
    public class LocationTests : IAsyncLifetime
    {
        private readonly CardDbContext _dbContext;

        public LocationTests()
        {
            _dbContext = TestFactory.CardDbContext();
        }

        public async Task InitializeAsync()
        {
            await _dbContext.SeedAsync();
        }

        public async Task DisposeAsync()
        {
            await _dbContext.DisposeAsync();
        }


        [Fact]
        public async Task First_IsShared_CorrectType()
        {
            var shared = await _dbContext.Locations.FirstAsync(l => l is Box);

            Assert.IsType<Box>(shared);
        }


        [Fact]
        public async Task First_IsDeck_CorrectType()
        {
            var deck = await _dbContext.Locations.FirstAsync(l => l is Deck);

            Assert.IsType<Deck>(deck);
        }


        [Fact]
        public async Task First_MultipleQueries_SameReference()
        {
            var deck1 = await _dbContext.Locations.FirstAsync(l => l is Deck);
            var deck2 = await _dbContext.Locations.FirstAsync(l => l is Deck);

            Assert.Same(deck1, deck2);
        }
    }
}