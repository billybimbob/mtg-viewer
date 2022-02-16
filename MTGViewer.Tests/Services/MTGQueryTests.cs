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
    public async Task All_PagedQuery_EqualPage()
    {
        const int page = 1;

        var result = await _mtgQuery
            .Where(x => x.Page == page)
            .SearchAsync();

        Assert.Equal(page, result.Offset.Current);
    }


    [Fact(Skip = "Calls external api")]
    public async Task Collection_MultipleIds_AllReturned()
    {
        int idBase = int.Parse(TEST_ID);

        var multiverseIds = Enumerable
            .Range(0, 5)
            .Select(i => idBase + i)
            .Select(id => id.ToString())
            .ToArray();

        var cards = await _mtgQuery.CollectionAsync(multiverseIds);

        Assert.Contains(TEST_NAME, cards.Select(c => c.Name));
        Assert.Equal(multiverseIds.Length, cards.Count);
    }
}