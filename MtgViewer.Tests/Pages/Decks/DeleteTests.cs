using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MtgViewer.Data;
using MtgViewer.Pages.Decks;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Pages.Decks;

public class DeleteTests : IAsyncLifetime
{
    private readonly DeleteModel _deleteModel;
    private readonly CardDbContext _dbContext;
    private readonly ActionHandlerFactory _pageFactory;
    private readonly TestDataGenerator _testGen;

    public DeleteTests(
        DeleteModel deleteModel,
        CardDbContext dbContext,
        ActionHandlerFactory pageFactory,
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

    private IQueryable<Hold> DeckHolds(Deck deck) =>
        _dbContext.Decks
            .Where(d => d.Id == deck.Id)
            .SelectMany(d => d.Holds)
            .AsNoTracking();

    [Fact]
    public async Task OnPost_WrongUser_NoChange()
    {
        // Arrange
        var deck = await _testGen.CreateDeckAsync();
        var wrongUser = await _dbContext.Players.FirstAsync(p => p.Id != deck.OwnerId);

        await _pageFactory.AddPageContextAsync(_deleteModel, wrongUser.Id);

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

        await _pageFactory.AddPageContextAsync(_deleteModel, deck.OwnerId);

        int wrongDeck = await _dbContext.Decks
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
    public async Task OnPost_ValidDeck_ReturnsCards(int copies)
    {
        // Arrange
        var deck = await _testGen.CreateDeckAsync(copies);

        await _pageFactory.AddPageContextAsync(_deleteModel, deck.OwnerId);

        int deckCardTotal = await DeckHolds(deck).SumAsync(h => h.Copies);

        var boxTotal = _dbContext.Holds
            .Where(h => h.Location is Box)
            .Select(h => h.Copies);

        // Act
        int boxBefore = await boxTotal.SumAsync();
        var result = await _deleteModel.OnPostAsync(deck.Id, null, default);

        int boxAfter = await boxTotal.SumAsync();
        var deckAfter = await Deck(deck).SingleOrDefaultAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Null(deckAfter);
        Assert.Equal(deckCardTotal, boxAfter - boxBefore);
    }
}
