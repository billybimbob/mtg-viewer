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

        private readonly MTGFetchService _fetch;

        public MTGFetchTests(MTGFetchService fetch)
        {
            _fetch = fetch;
        }


        [Fact(Skip = "Calls external api")]
        public async Task Search_NoParams_ReturnsEmpty()
        {
            _fetch.Reset();
            var cards = await _fetch.SearchAsync();

            Assert.Empty(cards);
        }


        [Fact(Skip = "Calls external api")]
        public async Task Search_NameParam_ReturnsSameName()
        {
            var cards = await _fetch
                .Where(c => c.Name, TEST_NAME)
                .SearchAsync();

            var cardNames = cards.Select(c => c.Name);

            Assert.Contains(TEST_NAME, cardNames);
        }


        [Fact(Skip = "Calls external api")]
        public async Task Find_Id_ReturnsCard()
        {
            var card = await _fetch.FindAsync(TEST_ID);

            Assert.Equal(TEST_NAME, card.Name);
        }


        [Fact(Skip = "Calls external api")]
        public async Task Find_NoId_ReturnsNull()
        {
            var card = await _fetch.FindAsync(null);

            Assert.Null(card);
        }


        [Fact(Skip = "Calls external api")]
        public async Task Find_Cache_ReturnsCard()
        {
            var testCard = await _fetch.FindAsync(TEST_ID);

            var provider = new MtgServiceProvider();
            var cache = new DataCacheService(Mock.Of<IConfiguration>(), Mock.Of<ILogger<DataCacheService>>());
            var cacheFetch = new MTGFetchService(provider, cache, Mock.Of<ILogger<MTGFetchService>>());

            cache[testCard.MultiverseId] = testCard;

            var card = await cacheFetch.FindAsync(TEST_ID);

            Assert.Equal(TEST_ID, testCard.MultiverseId);
            Assert.Equal(TEST_NAME, card.Name);
        }


        [Fact(Skip = "Calls external api")]
        public async Task Match_Id_ReturnsCard()
        {
            var search = new Card
            {
                MultiverseId = TEST_ID
            };

            var cards = await _fetch.MatchAsync(search);
            var cardNames = cards.Select(c => c.Name);

            Assert.Contains(TEST_NAME, cardNames);
        }


        [Fact(Skip = "Calls external api")]
        public async Task Match_Empty_ReturnsEmpty()
        {
            var search = new Card();

            var cards = await _fetch.MatchAsync(search);

            Assert.Empty(cards);
        }


        [Fact(Skip = "Calls external api")]
        public async Task Match_OnlyName_ReturnsCard()
        {
            var search = new Card
            {
                Name = TEST_NAME
            };

            var cards = await _fetch.MatchAsync(search);
            var cardNames = cards.Select(c => c.Name);

            Assert.Contains(TEST_NAME, cardNames);
        }
    }
}