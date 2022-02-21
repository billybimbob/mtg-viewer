using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Decks;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Decks;


public class DeleteTests : IAsyncLifetime
{
    private readonly DeleteModel _deleteModel;
    private readonly CardDbContext _dbContext;
    private readonly PageContextFactory _pageFactory;
    private readonly TestDataGenerator _testGen;


    public DeleteTests(
        DeleteModel deleteModel,
        CardDbContext dbContext,
        PageContextFactory pageFactory,
        TestDataGenerator testGen)
    {
        _deleteModel = deleteModel;
        _dbContext = dbContext;
        _pageFactory = pageFactory;
        _testGen = testGen;
    }


    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();


    private IQueryable<Deck> Deck(Deck deck) =>
        _dbContext.Decks
            .Where(d => d.Id == deck.Id)
            .AsNoTracking();


    private IQueryable<Amount> DeckCards(Deck deck) =>
        _dbContext.Amounts
            .Where(a => a.LocationId == deck.Id)
            .AsNoTracking();



    [Fact]
    public async Task OnPost_WrongUser_NoChange()
    {
        // Arrange
        var deck = await _testGen.CreateDeckAsync();
        var wrongUser = await _dbContext.Users.FirstAsync(u => u.Id != deck.OwnerId);

        await _pageFactory.AddModelContextAsync(_deleteModel, wrongUser.Id);

        // Act
        var result = await _deleteModel.OnPostAsync(deck.Id, null, default);
        var deckAfter = await Deck(deck).SingleOrDefaultAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.NotNull(deckAfter);
    }


    [Fact]
    public async Task OnPost_InvalidDeck_NoChange()
    {
        // Arrange
        var deck = await _testGen.CreateDeckAsync();

        await _pageFactory.AddModelContextAsync(_deleteModel, deck.OwnerId);

        var wrongDeck = await _dbContext.Decks
            .Where(d => d.OwnerId != deck.OwnerId)
            .Select(d => d.Id)
            .FirstAsync();

        // Act
        var result = await _deleteModel.OnPostAsync(wrongDeck, null, default);
        var deckAfter = await Deck(deck).SingleOrDefaultAsync();

        // // Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.NotNull(deckAfter);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task OnPost_ValidDeck_ReturnsCards(int numCopies)
    {
        // Arrange
        var deck = await _testGen.CreateDeckAsync(numCopies);

        await _pageFactory.AddModelContextAsync(_deleteModel, deck.OwnerId);

        var deckCardTotal = await DeckCards(deck).SumAsync(a => a.NumCopies);

        var boxTotal = _dbContext.Amounts
            .Where(a => a.Location is Box)
            .Select(a => a.NumCopies);

        // Act
        var boxBefore = await boxTotal.SumAsync();
        var result = await _deleteModel.OnPostAsync(deck.Id, null, default);

        var boxAfter = await boxTotal.SumAsync();
        var deckAfter = await Deck(deck).SingleOrDefaultAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Null(deckAfter);
        Assert.Equal(deckCardTotal, boxAfter - boxBefore);
    }
}