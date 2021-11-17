using System.Linq;
using System.Threading.Tasks;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Tests.Services;


public class MTGFetchTests
{
    private const string TEST_ID = "386616";
    private const string TEST_NAME = "Narset, Enlightened Master";

    private readonly MTGFetchService _fetch;

    public MTGFetchTests(MTGFetchService fetch)
    {
        _fetch = fetch;
    }


    [Fact]
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

        var card = await _fetch.FindAsync(TEST_ID);

        Assert.Equal(TEST_ID, testCard.MultiverseId);
        Assert.Equal(TEST_NAME, card.Name);
    }


    [Fact(Skip = "Calls external api")]
    public async Task Where_MultiId_ReturnsSingleCard()
    {
        var search = new CardSearch
        {
            MultiverseId = TEST_ID
        };

        var cards = await _fetch
            .Where(search)
            .SearchAsync();

        var cardName = cards.Single();

        Assert.Contains(TEST_NAME, cardName.Name);
    }


    [Fact]
    public async Task Where_Empty_ReturnsEmpty()
    {
        var search = new CardSearch();

        var cards = await _fetch
            .Where(search)
            .SearchAsync();

        Assert.Empty(cards);
    }


    [Fact(Skip = "Calls external api")]
    public async Task Where_OnlyName_ReturnsCard()
    {
        var search = new CardSearch
        {
            Name = TEST_NAME
        };

        var cards = await _fetch
            .Where(search)
            .SearchAsync();

        var cardNames = cards.Select(c => c.Name);

        Assert.Contains(TEST_NAME, cardNames);
    }


    [Fact(Skip = "Calls external api")]
    public async Task All_PagedQuery_EqualPageSize()
    {
        const int pageSize = 10;
        const int page = 1;

        var result = await _fetch
            .Where(x => x.Page, page)
            .Where(x => x.PageSize, pageSize)
            .SearchAsync();

        Assert.True(pageSize >= result.Count);
        Assert.Equal(page, result.Pages.Current);
    }
}