using System.Linq;
using System.Threading.Tasks;
using Xunit;

using MTGViewer.Services;

namespace MTGViewer.Tests.Services;


public class MTGQueryTests
{
    private const string TEST_ID = "386616";
    private const string TEST_NAME = "Narset, Enlightened Master";

    private readonly IMTGQuery _mtgQuery;

    public MTGQueryTests(IMTGQuery query)
    {
        _mtgQuery = query;
    }


    [Fact]
    public async Task Search_NoParams_ReturnsEmpty()
    {
        _mtgQuery.Reset();
        var cards = await _mtgQuery.SearchAsync();

        Assert.Empty(cards);
    }


    [Fact(Skip = "Calls external api")]
    public async Task Search_NameParam_ReturnsSameName()
    {
        var cards = await _mtgQuery
            .Where(c => c.Name == TEST_NAME)
            .SearchAsync();

        var cardNames = cards.Select(c => c.Name);

        Assert.Contains(TEST_NAME, cardNames);
    }


    private string GetName() => TEST_NAME;


    [Fact(Skip = "Calls external api")]
    public async Task Search_NameParamCall_ReturnsSameName()
    { 
        var cards = await _mtgQuery
            .Where(c => c.Name == GetName())
            .SearchAsync();

        var cardNames = cards.Select(c => c.Name);

        Assert.Contains(TEST_NAME, cardNames);
    }



    [Fact(Skip = "Calls external api")]
    public async Task Find_Id_ReturnsCard()
    {
        var card = await _mtgQuery.FindAsync(TEST_ID);

        Assert.NotNull(card);
        Assert.Equal(TEST_NAME, card!.Name);
    }


    [Fact(Skip = "Calls external api")]
    public async Task Find_NoId_ReturnsNull()
    {
        var card = await _mtgQuery.FindAsync(null!);

        Assert.Null(card);
    }


    [Fact(Skip = "Calls external api")]
    public async Task Find_Cache_ReturnsCard()
    {
        var testCard = await _mtgQuery.FindAsync(TEST_ID);

        var card = await _mtgQuery.FindAsync(TEST_ID);

        Assert.NotNull(testCard);
        Assert.NotNull(card);

        Assert.Equal(TEST_ID, testCard!.MultiverseId);
        Assert.Equal(TEST_NAME, card!.Name);
    }


    [Fact(Skip = "Calls external api")]
    public async Task Where_MultiId_ReturnsSingleCard()
    {
        var search = new CardQuery
        {
            MultiverseId = TEST_ID
        };

        var cards = await _mtgQuery
            .Where(search)
            .SearchAsync();

        var cardName = cards.Single();

        Assert.Contains(TEST_NAME, cardName.Name);
    }


    [Fact]
    public async Task Where_Empty_ReturnsEmpty()
    {
        var search = new CardQuery();

        var cards = await _mtgQuery
            .Where(search)
            .SearchAsync();

        Assert.Empty(cards);
    }


    [Fact(Skip = "Calls external api")]
    public async Task Where_OnlyName_ReturnsCard()
    {
        var search = new CardQuery
        {
            Name = TEST_NAME
        };

        var cards = await _mtgQuery
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

        var result = await _mtgQuery
            .Where(x => x.Page == page)
            .Where(x => x.PageSize == pageSize)
            .SearchAsync();

        Assert.True(pageSize >= result.Count);
        Assert.Equal(page, result.Offset.Current);
    }
}