using System.Threading.Tasks;

using Xunit;
using Moq;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Service;
using MTGViewer.Data;
using MTGViewer.Services;


namespace MTGViewer.Tests.Services
{
    public class MTGFetchTests
    {
        private const string TEST_ID = "386616";


        [Fact]
        public async Task Search_NoParams_ReturnsCards()
        {
            var provider = new MtgServiceProvider();
            var cache = new Mock<DataCacheService>(Mock.Of<IConfiguration>(), Mock.Of<ILogger<DataCacheService>>());

            var fetch = new MTGFetchService(provider, cache.Object, Mock.Of<ILogger<MTGFetchService>>());

            fetch.Reset();
            var cards = await fetch.SearchAsync();

            Assert.NotEmpty(cards);
        }


        [Fact]
        public async Task GetId_NoCache_ReturnsCard()
        {
            var provider = new MtgServiceProvider();
            var cache = new Mock<DataCacheService>(Mock.Of<IConfiguration>(), Mock.Of<ILogger<DataCacheService>>());

            var fetch = new MTGFetchService(provider, cache.Object, Mock.Of<ILogger<MTGFetchService>>());

            var card = await fetch.GetIdAsync(TEST_ID);

            Assert.Equal(card.Name, "Narset, Enlightened Master");
        }


        [Fact]
        public async Task GetId_NoId_ReturnsNull()
        {
            var provider = new MtgServiceProvider();
            var cache = new Mock<DataCacheService>(Mock.Of<IConfiguration>(), Mock.Of<ILogger<DataCacheService>>());

            var fetch = new MTGFetchService(provider, cache.Object, Mock.Of<ILogger<MTGFetchService>>());

            var card = await fetch.GetIdAsync(null);

            Assert.Null(card);
        }
    }
}