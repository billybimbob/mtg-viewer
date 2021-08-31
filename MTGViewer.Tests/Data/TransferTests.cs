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
        public async Task First_IsSuggestion_CorrectType()
        {
            await using var dbContext = TestFactory.CardDbContext();
            await dbContext.SeedAsync();

            var suggestion = await dbContext.Transfers.FirstAsync(t => t is Suggestion);

            Assert.IsType<Suggestion>(suggestion);
        }


        [Fact]
        public async Task First_IsTrade_CorrectType()
        {
            await using var dbContext = TestFactory.CardDbContext();
            await dbContext.SeedAsync();

            var trade = await dbContext.Transfers.FirstAsync(t => t is Trade);

            Assert.IsType<Trade>(trade);
        }
    }
}