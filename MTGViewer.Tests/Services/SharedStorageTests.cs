using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;
using Moq;

using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Services
{
    public class SharedStorageTests : IAsyncLifetime
    {
        private readonly ServiceProvider _services;
        private readonly CardDbContext _dbContext;
        private readonly ISharedStorage _sharedStorage;


        public SharedStorageTests()
        {
            _services = TestFactory.ServiceProvider();
            _dbContext = TestFactory.CardDbContext(_services);

            _sharedStorage = new ExpandableSharedService(
                Mock.Of<IConfiguration>(), _dbContext);
        }


        public async Task InitializeAsync()
        {
            await _dbContext.SeedAsync();
        }


        public async Task DisposeAsync()
        {
            await _services.DisposeAsync();
            await _dbContext.DisposeAsync();
        }


        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task Return_ValidCard_Success(int cardIndex)
        {
            var copies = 4;
            var card = await _dbContext.Cards
                .AsNoTracking()
                .Skip(cardIndex)
                .FirstAsync();

            var sharedQuery = _dbContext.Amounts
                .Where(ca => ca.Location is Shared && ca.CardId == card.Id)
                .Include(ca => ca.Location)
                .AsNoTracking();

            var sharedBefore = await sharedQuery.FirstOrDefaultAsync();
            var beforeAmount = sharedBefore?.Amount ?? 0;

            await _sharedStorage.ReturnAsync(card, copies);

            var sharedAfter = await sharedQuery.FirstAsync();

            Assert.IsType<Shared>(sharedAfter.Location);

            Assert.Equal(card.Id, sharedAfter.CardId);
            Assert.Equal(copies, sharedAfter.Amount - beforeAmount);
        }


        // [Fact]
        // public async Task Return_LargeAmount_SplitMultiple()
        // {
        // }


        [Fact]
        public async Task Optimize_LargeAmounts_SplitMultiple()
        {
            var card = await _dbContext.Cards.AsNoTracking().FirstAsync();
            var copies = 120;

            var spotQuery = _dbContext.Amounts
                .Where(ca => ca.Location is Shared && ca.CardId == card.Id)
                .Include(ca => ca.Location)
                .AsNoTracking();

            await _sharedStorage.ReturnAsync(card, copies);
            var oldSpots = await spotQuery.ToListAsync();

            await _sharedStorage.OptimizeAsync();
            var newSpots = await spotQuery.ToListAsync();

            Assert.All(newSpots, ca => Assert.IsType<Shared>(ca.Location));
            Assert.All(newSpots, ca => Assert.True(ca.Amount < copies));

            Assert.Equal(
                oldSpots.Select(ca => ca.Amount).Sum(),
                newSpots.Select(ca => ca.Amount).Sum());
        }
    }
}