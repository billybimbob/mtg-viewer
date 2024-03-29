using System.Linq;
using System.Threading.Tasks;

using Xunit;

using MtgApiManager.Lib.Model;

using MtgViewer.Services.Search;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Services;

public class MtgQueryTests
{
    private readonly TestMtgApiQuery _mtgQuery;

    public MtgQueryTests(TestMtgApiQuery mtgQuery)
    {
        _mtgQuery = mtgQuery;
    }

    [Fact]
    public async Task Search_NameParam_ReturnsSameName()
    {
        var testCard = await _mtgQuery.SourceCards.FirstAsync();

        var search = new CardSearch
        {
            Name = testCard.Name
        };

        var cards = await _mtgQuery.SearchAsync(search);

        var cardNames = cards.Select(c => c.Name);

        Assert.Contains(testCard.Name, cardNames);
    }

    private static string GetName(ICard card) => card.Name;

    [Fact]
    public async Task Search_NameParamCall_ReturnsSameName()
    {
        var testCard = await _mtgQuery.SourceCards.FirstAsync();

        var search = new CardSearch
        {
            Name = GetName(testCard)
        };

        var cards = await _mtgQuery.SearchAsync(search);

        var cardNames = cards.Select(c => c.Name);

        Assert.Contains(testCard.Name, cardNames);
    }

    [Fact]
    public async Task Search_SplitName_ReturnsWithFlip()
    {
        var splitCard = await _mtgQuery.FlipCards.FirstAsync();

        var search = new CardSearch
        {
            Name = splitCard.Name
        };

        var cards = await _mtgQuery.SearchAsync(search);

        var first = cards[0];

        Assert.NotNull(first.Flip);
        Assert.Equal(splitCard.Name, first.Name);
    }

    [Fact]
    public async Task Find_Id_ReturnsCard()
    {
        var testCard = await _mtgQuery.SourceCards.FirstAsync();

        var card = await _mtgQuery.FindAsync(testCard.MultiverseId);

        Assert.NotNull(card);
        Assert.Equal(testCard.Name, card!.Name);
    }

    [Fact]
    public async Task Find_NoId_ReturnsNull()
    {
        var card = await _mtgQuery.FindAsync(null!);

        Assert.Null(card);
    }

    [Fact]
    public async Task Find_SplitId_ReturnsWithFlip()
    {
        var splitCard = await _mtgQuery.FlipCards.FirstAsync();

        var card = await _mtgQuery.FindAsync(splitCard.MultiverseId);

        Assert.NotNull(card);
        Assert.NotNull(card!.Flip);

        Assert.Equal(splitCard.Name, card!.Name);
        Assert.Equal(splitCard.MultiverseId, card!.MultiverseId);
    }

    [Fact]
    public async Task Search_PagedQuery_EqualPage()
    {
        const int page = 1;

        var result = await _mtgQuery.SearchAsync(
            new CardSearch
            {
                Page = page
            });

        Assert.Equal(page, result.Offset.Current);
    }

    [Fact]
    public async Task Collection_MultipleIds_AllReturned()
    {
        const int targetSize = 5;

        string[] multiverseIds = await _mtgQuery.SourceCards
            .Take(targetSize)
            .Select(c => c.MultiverseId)
            .ToArrayAsync();

        var cardNames = await _mtgQuery
            .CollectionAsync(multiverseIds)
            .Select(c => c.Name)
            .ToListAsync();

        Assert.Equal(multiverseIds.Length, cardNames.Count);
    }
}
