using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Decks;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Decks;

public class ExchangeTests : IAsyncLifetime
{
    private readonly ExchangeModel _exchangeModel;
    private readonly CardDbContext _dbContext;
    private readonly PageContextFactory _pageFactory;
    private readonly TestDataGenerator _testGen;

    public ExchangeTests(
        ExchangeModel exchangeModel,
        CardDbContext dbContext,
        PageContextFactory pageFactory,
        TestDataGenerator testGen)
    {
        _exchangeModel = exchangeModel;
        _dbContext = dbContext;
        _pageFactory = pageFactory;
        _testGen = testGen;
    }

    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();

    private IQueryable<string> OwnerId(Quantity quantity) =>
        _dbContext.Decks
            .Where(d => d.Id == quantity.LocationId)
            .Select(d => d.OwnerId);

    private IQueryable<int> Copies(Want want) =>
        _dbContext.Wants
            .Where(w => w.Id == want.Id)
            .Select(w => w.Copies);

    private IQueryable<int> Copies(Giveback give) =>
        _dbContext.Givebacks
            .Where(g => g.Id == give.Id)
            .Select(g => g.Copies);

    private IQueryable<int> HoldCopies(Quantity quantity) =>
        _dbContext.Holds
            .Where(h => h.LocationId == quantity.LocationId
                && h.CardId == quantity.CardId)
            .Select(h => h.Copies);

    private IQueryable<int> BoxCardCopies(Quantity quantity) =>
        _dbContext.Holds
            .Where(h => h.Location is Box && h.CardId == quantity.CardId)
            .Select(h => h.Copies);

    private IQueryable<int> ChangeCopies(Quantity quantity) =>
        _dbContext.Changes
            .Where(c => c.ToId == quantity.LocationId || c.FromId == quantity.LocationId)
            .Select(c => c.Copies);

    [Fact]
    public async Task OnPost_InvalidDeck_NotFound()
    {
        var invalidDeckId = 0;
        var validUserId = await _dbContext.Users.Select(u => u.Id).FirstAsync();

        await _pageFactory.AddModelContextAsync(_exchangeModel, validUserId);

        var result = await _exchangeModel.OnPostAsync(invalidDeckId, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPost_InvalidUser_NotFound()
    {
        var deck = await _dbContext.Decks
            .AsNoTracking()
            .FirstAsync();

        var invalidUserId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync(uid => uid != deck.OwnerId);

        await _pageFactory.AddModelContextAsync(_exchangeModel, invalidUserId);

        var result = await _exchangeModel.OnPostAsync(deck.Id, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPost_ValidWant_AppliesWant()
    {
        // Arrange
        var want = await _testGen.GetWantAsync();
        var targetCopies = want.Copies;
        var deckOwnerId = await OwnerId(want).SingleAsync();

        await _pageFactory.AddModelContextAsync(_exchangeModel, deckOwnerId);

        // Act
        var wantBefore = await Copies(want).SingleAsync();
        var holdBefore = await HoldCopies(want).SingleOrDefaultAsync();

        var boxBefore = await BoxCardCopies(want).SumAsync();
        var changeBefore = await ChangeCopies(want).SumAsync();

        var result = await _exchangeModel.OnPostAsync(want.LocationId, default);

        var wantAfter = await Copies(want).SingleOrDefaultAsync();
        var holdAfter = await HoldCopies(want).SingleAsync();

        var boxAfter = await BoxCardCopies(want).SumAsync();
        var changeAfter = await ChangeCopies(want).SumAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(targetCopies, wantBefore - wantAfter);
        Assert.Equal(targetCopies, holdAfter - holdBefore);

        Assert.Equal(targetCopies, boxBefore - boxAfter);
        Assert.Equal(targetCopies, changeAfter - changeBefore);
    }

    [Fact]
    public async Task OnPost_InsufficientWant_WantLowered()
    {
        // Arrange
        var targetMod = 2;
        var want = await _testGen.GetWantAsync(targetMod);

        var targetLimit = want.Copies - targetMod;
        var deckOwnerId = await OwnerId(want).SingleAsync();

        await _pageFactory.AddModelContextAsync(_exchangeModel, deckOwnerId);

        // Act
        int wantBefore = await Copies(want).SingleAsync();
        int holdBefore = await HoldCopies(want).SingleOrDefaultAsync();

        int boxBefore = await BoxCardCopies(want).SumAsync();
        int changeBefore = await ChangeCopies(want).SumAsync();

        var result = await _exchangeModel.OnPostAsync(want.LocationId, default);

        int wantAFter = await Copies(want).SingleAsync();
        int holdAfter = await HoldCopies(want).SingleAsync();

        int boxAfter = await BoxCardCopies(want).SumAsync();
        int changeAfter = await ChangeCopies(want).SumAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(targetLimit, wantBefore - wantAFter);
        Assert.Equal(targetLimit, holdAfter - holdBefore);

        Assert.Equal(targetLimit, boxBefore - boxAfter);
        Assert.Equal(targetLimit, changeAfter - changeBefore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task OnPost_ValidGive_AppliesGive(int targetMod)
    {
        // Arrange
        var request = await _testGen.GetGivebackAsync(targetMod);
        int returnCopies = request.Copies;
        var deckOwnerId = await OwnerId(request).SingleAsync();

        await _pageFactory.AddModelContextAsync(_exchangeModel, deckOwnerId);

        // Act
        int giveBefore = await Copies(request).SingleAsync();
        int holdBefore = await HoldCopies(request).SingleAsync();

        int boxBefore = await BoxCardCopies(request).SumAsync();
        int changeBefore = await ChangeCopies(request).SumAsync();

        var result = await _exchangeModel.OnPostAsync(request.LocationId, default);

        int giveAfter = await Copies(request).SingleOrDefaultAsync();
        int holdAfter = await HoldCopies(request).SingleOrDefaultAsync();

        int boxAfter = await BoxCardCopies(request).SumAsync();
        int changeAfter = await ChangeCopies(request).SumAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(returnCopies, giveBefore - giveAfter);
        Assert.Equal(returnCopies, holdBefore - holdAfter);

        Assert.Equal(returnCopies, boxAfter - boxBefore);
        Assert.Equal(returnCopies, changeAfter - changeBefore);
    }

    [Fact]
    public async Task OnPost_TradeActive_NoChange()
    {
        var giveBack = await _testGen.GetGivebackAsync(2);
        var deckOwnerId = await OwnerId(giveBack).SingleAsync();

        var tradeTarget = await _dbContext.Holds
            .Where(h => h.Location is Deck
                && (h.Location as Deck)!.OwnerId != deckOwnerId)
            .Select(h => h.Location)
            .FirstAsync();

        var activeTrade = new Trade
        {
            Card = giveBack.Card,
            To = (Deck)giveBack.Location,
            From = (Deck)tradeTarget,
            Copies = 3
        };

        _dbContext.Trades.Attach(activeTrade);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        await _pageFactory.AddModelContextAsync(_exchangeModel, deckOwnerId);

        int boxBefore = await BoxCardCopies(giveBack).SumAsync();
        int holdBefore = await HoldCopies(giveBack).SingleAsync();

        var result = await _exchangeModel.OnPostAsync(giveBack.LocationId, default);

        int boxAfter = await BoxCardCopies(giveBack).SumAsync();
        int holdAfter = await HoldCopies(giveBack).SingleAsync();

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(boxBefore, boxAfter);
        Assert.Equal(holdBefore, holdAfter);
    }

    [Fact]
    public async Task OnPost_MixedWantGives_AppliesChanges()
    {
        var (want, give) = await _testGen.GetMixedRequestDeckAsync();
        var deckOwnerId = await OwnerId(want).SingleAsync();

        await _pageFactory.AddModelContextAsync(_exchangeModel, deckOwnerId);

        int wantTarget = want.Copies;
        int giveTarget = give.Copies;

        int holdWantBefore = await HoldCopies(want).SingleOrDefaultAsync();
        int holdGiveBefore = await HoldCopies(give).SingleAsync();

        var result = await _exchangeModel.OnPostAsync(want.LocationId, default);

        int holdWantAfter = await HoldCopies(want).SingleAsync();
        int holdGiveAfter = await HoldCopies(give).SingleOrDefaultAsync();

        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(want.LocationId, give.LocationId);
        Assert.Equal(wantTarget, holdWantAfter - holdWantBefore);
        Assert.Equal(giveTarget, holdGiveBefore - holdGiveAfter);
    }
}
