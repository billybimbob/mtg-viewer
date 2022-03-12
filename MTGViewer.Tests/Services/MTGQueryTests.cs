using System.Linq;
using System.Threading.Tasks;
using Xunit;

using MTGViewer.Services;

namespace MTGViewer.Tests.Services;


public class MTGQueryTests
{
    private const string TestId = "386616";
    private const string TestName = "Narset, Enlightened Master";

    private const string SplitId = "27163";
    private const string SplitName = "Illusion // Reality";


    private readonly IMTGQuery _mtgQuery;

    public MTGQueryTests(IMTGQuery query)
    {
        _mtgQuery = query;
    }



    [Fact(Skip = "Calls external api")]
    public async Task Search_NameParam_ReturnsSameName()
    {
        var cards = await _mtgQuery
            .Where(c => c.Name == TestName)
            .SearchAsync();

        var cardNames = cards.Select(c => c.Name);

        Assert.Contains(TestName, cardNames);
    }


    private string GetName() => TestName;


    [Fact(Skip = "Calls external api")]
    public async Task Search_NameParamCall_ReturnsSameName()
    { 
        var cards = await _mtgQuery
            .Where(c => c.Name == GetName())
            .SearchAsync();

        var cardNames = cards.Select(c => c.Name);

        Assert.Contains(TestName, cardNames);
    }


    [Fact(Skip = "Calls external api")]
    public async Task Search_SplitName_ResturnsWithFlip()
    {
        var cards = await _mtgQuery
            .Where(c => c.Name == SplitName)
            .SearchAsync();

        var first = cards.FirstOrDefault();

        Assert.NotNull(first);
        Assert.NotNull(first!.Flip);

        Assert.Equal(SplitName, first!.Name);
        Assert.Equal(SplitId, first!.MultiverseId);
    }



    [Fact(Skip = "Calls external api")]
    public async Task Find_Id_ReturnsCard()
    {
        var card = await _mtgQuery.FindAsync(TestId);

        Assert.NotNull(card);
        Assert.Equal(TestName, card!.Name);
    }


    [Fact(Skip = "Calls external api")]
    public async Task Find_NoId_ReturnsNull()
    {
        var card = await _mtgQuery.FindAsync(null!);

        Assert.Null(card);
    }


    [Fact(Skip = "Calls external api")]
    public async Task Find_SplitId_ReturnsWithFlip()
    {
        var card = await _mtgQuery.FindAsync(SplitId);

        Assert.NotNull(card);
        Assert.NotNull(card!.Flip);

        Assert.Equal(SplitName, card!.Name);
        Assert.Equal(SplitId, card!.MultiverseId);
    }


    // [Fact(Skip = "Calls external api")]
    // public async Task Find_Cache_ReturnsCard()
    // {
    //     var testCard = await _mtgQuery.FindAsync(TestId);

    //     var card = await _mtgQuery.FindAsync(TestId);

    //     Assert.NotNull(testCard);
    //     Assert.NotNull(card);

    //     Assert.Equal(TestId, testCard!.MultiverseId);
    //     Assert.Equal(TestName, card!.Name);
    // }


    [Fact(Skip = "Calls external api")]
    public async Task Search_PagedQuery_EqualPage()
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
        int idBase = int.Parse(TestId);

        var multiverseIds = Enumerable
            .Range(0, 5)
            .Select(i => idBase + i)
            .Select(id => id.ToString())
            .ToArray();

        var cardNames = await _mtgQuery
            .Collection(multiverseIds)
            .Select(c => c.Name)
            .ToListAsync();

        Assert.Contains(TestName, cardNames);
        Assert.Equal(multiverseIds.Length, cardNames.Count);
    }
}