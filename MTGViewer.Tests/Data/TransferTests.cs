using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Data
{
    public class TradeTests : IAsyncLifetime
    {
        private readonly CardDbContext _dbContext;

        public TradeTests()
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
        public async Task First_IsSuggestion_CorrectType()
        {
            var suggestion = await _dbContext.Transfers.FirstAsync(t => t is Suggestion);

            Assert.IsType<Suggestion>(suggestion);
        }


        [Fact]
        public async Task First_IsTrade_CorrectType()
        {
            var trade = await _dbContext.Transfers.FirstAsync(t => t is Trade);

            Assert.IsType<Trade>(trade);
        }
    }
}