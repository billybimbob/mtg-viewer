using System.Linq;
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
        private const string TEST_NAME = "Narset, Enlightened Master";


        private MTGFetchService GetNoCacheFetchService()
        {
            var provider = new MtgServiceProvider();
            var cache = new DataCacheService(Mock.Of<IConfiguration>(), Mock.Of<ILogger<DataCacheService>>());

            return new MTGFetchService(provider, cache, Mock.Of<ILogger<MTGFetchService>>());
        }


        [Fact]
        public async Task Search_NoParams_ReturnsEmpty()
        {
            var fetch = GetNoCacheFetchService();

            fetch.Reset();
            var cards = await fetch.SearchAsync();

            Assert.Empty(cards);
        }


        [Fact]
        public async Task Search_NameParam_ReturnsSameName()
        {
            var fetch = GetNoCacheFetchService();

            var cards = await fetch
                .Where(c => c.Name, TEST_NAME)
                .SearchAsync();

            var cardNames = cards.Select(c => c.Name);

            Assert.Contains(TEST_NAME, cardNames);
        }


        [Fact]
        public async Task Find_Id_ReturnsCard()
        {
            var fetch = GetNoCacheFetchService();

            var card = await fetch.FindAsync(TEST_ID);

            Assert.Equal(card.Name, TEST_NAME);
        }


        [Fact]
        public async Task Find_NoId_ReturnsNull()
        {
            var fetch = GetNoCacheFetchService();

            var card = await fetch.FindAsync(null);

            Assert.Null(card);
        }


        [Fact]
        public async Task Find_Cache_ReturnsCard()
        {
            var noCacheFetch = GetNoCacheFetchService();
            var testCard = await noCacheFetch.FindAsync(TEST_ID);

            var provider = new MtgServiceProvider();
            var cache = new DataCacheService(Mock.Of<IConfiguration>(), Mock.Of<ILogger<DataCacheService>>());

            cache[testCard.MultiverseId] = testCard;

            var fetch = new MTGFetchService(provider, cache, Mock.Of<ILogger<MTGFetchService>>());

            var card = await fetch.FindAsync(TEST_ID);

            Assert.Equal(card.Name, TEST_NAME);
        }


        [Fact]
        public async Task Match_Id_ReturnsCard()
        {
            var fetch = GetNoCacheFetchService();
            var search = new Card
            {
                MultiverseId = TEST_ID
            };

            var cards = await fetch.MatchAsync(search);
            var cardNames = cards.Select(c => c.Name);

            Assert.Contains(TEST_NAME, cardNames);
        }


        [Fact]
        public async Task Match_Empty_ReturnsEmpty()
        {
            var fetch = GetNoCacheFetchService();
            var search = new Card();

            var cards = await fetch.MatchAsync(search);

            Assert.Empty(cards);
        }


        [Fact]
        public async Task Match_OnlyName_ReturnsCard()
        {
            var fetch = GetNoCacheFetchService();
            var search = new Card
            {
                Name = TEST_NAME
            };

            var cards = await fetch.MatchAsync(search);
            var cardNames = cards.Select(c => c.Name);

            Assert.Contains(TEST_NAME, cardNames);
        }
    }
}