using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;
using Xunit;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Pages.Decks;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Decks;


public class ExchangeTests : IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private readonly TestDataGenerator _testGen;

    private readonly ExchangeModel _exchangeModel;


    public ExchangeTests(
        CardDbContext dbContext,
        TreasuryHandler treasuryHandler,
        UserManager<CardUser> userManager,
        CardText cardText,
        TestDataGenerator testGen)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _testGen = testGen;

        var logger = Mock.Of<ILogger<ExchangeModel>>();

        _exchangeModel = new(
            _dbContext, treasuryHandler, _userManager, cardText, logger);
    }


    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();


    private IQueryable<string> OwnerId(Quantity quantity) =>
        _dbContext.Decks
            .Where(d => d.Id == quantity.LocationId)
            .Select(d => d.OwnerId);


    private IQueryable<int> NumCopies(Want request) =>
        _dbContext.Wants
            .Where(w => w.Id == request.Id)
            .Select(w => w.NumCopies);


    private IQueryable<int> NumCopies(GiveBack request) =>
        _dbContext.GiveBacks
            .Where(g => g.Id == request.Id)
            .Select(g => g.NumCopies);


    private IQueryable<int> ActualNumCopies(Quantity quantity) =>
        _dbContext.Amounts
            .Where(ca => ca.LocationId == quantity.LocationId
                && ca.CardId == quantity.CardId)
            .Select(ca => ca.NumCopies);


    private IQueryable<int> BoxNumCopies(Quantity quantity) =>
        _dbContext.Amounts
            .Where(ca => ca.Location is Box && ca.CardId == quantity.CardId)
            .Select(ca => ca.NumCopies);


    private IQueryable<int> ChangeAmount(Quantity quantity) =>
        _dbContext.Changes
            .Where(c => c.ToId == quantity.LocationId || c.FromId == quantity.LocationId)
            .Select(c => c.Amount);



    [Fact]
    public async Task OnPost_InvalidDeck_NotFound()
    {
        var invalidDeckId = 0;
        var validUserId = await _dbContext.Users.Select(u => u.Id).FirstAsync();

        await _exchangeModel.SetModelContextAsync(_userManager, validUserId);

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

        await _exchangeModel.SetModelContextAsync(_userManager, invalidUserId);

        var result = await _exchangeModel.OnPostAsync(deck.Id, default);

        Assert.IsType<NotFoundResult>(result);
    }


    [Fact]
    public async Task OnPost_ValidWant_AppliesWant()
    {
        // Arrange
        var want = await _testGen.GetWantAsync();
        var targetAmount = want.NumCopies;
        var deckOwnerId = await OwnerId(want).SingleAsync();

        await _exchangeModel.SetModelContextAsync(_userManager, deckOwnerId);

        // Act
        var wantBefore = await NumCopies(want).SingleAsync();
        var actualBefore = await ActualNumCopies(want).SingleOrDefaultAsync();

        var boxBefore = await BoxNumCopies(want).SumAsync();
        var changeBefore = await ChangeAmount(want).SumAsync();

        var result = await _exchangeModel.OnPostAsync(want.LocationId, default);

        var wantAfter = await NumCopies(want).SingleOrDefaultAsync();
        var actualAfter = await ActualNumCopies(want).SingleAsync();

        var boxAfter = await BoxNumCopies(want).SumAsync();
        var changeAfter = await ChangeAmount(want).SumAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(targetAmount, wantBefore - wantAfter);
        Assert.Equal(targetAmount, actualAfter - actualBefore);

        Assert.Equal(targetAmount, boxBefore - boxAfter);
        Assert.Equal(targetAmount, changeAfter - changeBefore);
    }


    [Fact]
    public async Task OnPost_InsufficientWant_WantLowered()
    {
        // Arrange
        var targetMod = 2;
        var request = await _testGen.GetWantAsync(targetMod);

        var targetLimit = request.NumCopies - targetMod;
        var deckOwnerId = await OwnerId(request).SingleAsync();

        await _exchangeModel.SetModelContextAsync(_userManager, deckOwnerId);

        // Act
        var wantBefore = await NumCopies(request).SingleAsync();
        var actualBefore = await ActualNumCopies(request).SingleOrDefaultAsync();

        var boxBefore = await BoxNumCopies(request).SumAsync();
        var changeBefore = await ChangeAmount(request).SumAsync();

        var result = await _exchangeModel.OnPostAsync(request.LocationId, default);

        var wantAFter = await NumCopies(request).SingleAsync();
        var actualAfter = await ActualNumCopies(request).SingleAsync();

        var boxAfter = await BoxNumCopies(request).SumAsync();
        var changeAfter = await ChangeAmount(request).SumAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(targetLimit, wantBefore - wantAFter);
        Assert.Equal(targetLimit, actualAfter - actualBefore);

        Assert.Equal(targetLimit, boxBefore - boxAfter);
        Assert.Equal(targetLimit, changeAfter - changeBefore);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task OnPost_ValidGive_AppliesGive(int targetMod)
    {
        // Arrange
        var request = await _testGen.GetGiveBackAsync(targetMod);
        var returnAmount = request.NumCopies;
        var deckOwnerId = await OwnerId(request).SingleAsync();

        await _exchangeModel.SetModelContextAsync(_userManager, deckOwnerId);

        // Act
        var giveBefore = await NumCopies(request).SingleAsync();
        var actualBefore = await ActualNumCopies(request).SingleAsync();

        var boxBefore = await BoxNumCopies(request).SumAsync();
        var changeBefore = await ChangeAmount(request).SumAsync();

        var result = await _exchangeModel.OnPostAsync(request.LocationId, default);

        var giveAfter = await NumCopies(request).SingleOrDefaultAsync();
        var actualAfter = await ActualNumCopies(request).SingleOrDefaultAsync();

        var boxAfter = await BoxNumCopies(request).SumAsync();
        var changeAfter = await ChangeAmount(request).SumAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(returnAmount, giveBefore - giveAfter);
        Assert.Equal(returnAmount, actualBefore - actualAfter);

        Assert.Equal(returnAmount, boxAfter - boxBefore);
        Assert.Equal(returnAmount, changeAfter - changeBefore);
    }


    [Fact]
    public async Task OnPost_InsufficientGive_NoChange()
    {
        // Arrange
        var request = await _testGen.GetGiveBackAsync(2);
        var deckOwnerId = await OwnerId(request).SingleAsync();

        await _exchangeModel.SetModelContextAsync(_userManager, deckOwnerId);

        // Act
        var giveBefore = await NumCopies(request).SingleAsync();
        var actualBefore = await ActualNumCopies(request).SingleAsync();

        var boxBefore = await BoxNumCopies(request).SumAsync();
        var changeBefore = await ChangeAmount(request).SumAsync();

        var result = await _exchangeModel.OnPostAsync(request.LocationId, default);

        var giveAfter = await NumCopies(request).SingleAsync();
        var actualAfter = await ActualNumCopies(request).SingleAsync();

        var boxAfter = await BoxNumCopies(request).SumAsync();
        var changeAfter = await ChangeAmount(request).SumAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(giveBefore, giveAfter);
        Assert.Equal(actualBefore, actualAfter);

        Assert.Equal(boxBefore, boxAfter);
        Assert.Equal(changeBefore, changeAfter);
    }


    [Fact]
    public async Task OnPost_TradeActive_NoChange()
    {
        var request = await _testGen.GetGiveBackAsync(2);
        var deckOwnerId = await OwnerId(request).SingleAsync();

        var tradeTarget = await _dbContext.Amounts
            .Where(ca => ca.Location is Deck
                && (ca.Location as Deck)!.OwnerId != deckOwnerId)
            .Select(ca => ca.Location)
            .FirstAsync();

        var activeTrade = new Trade
        {
            Card = request.Card,
            To = (Deck)request.Location,
            From = (Deck)tradeTarget,
            Amount = 3
        };

        _dbContext.Trades.Attach(activeTrade);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        await _exchangeModel.SetModelContextAsync(_userManager, deckOwnerId);

        var boxBefore = await BoxNumCopies(request).SumAsync();
        var actualBefore = await ActualNumCopies(request).SingleAsync();

        var result = await _exchangeModel.OnPostAsync(request.LocationId, default);

        var boxAfter = await BoxNumCopies(request).SumAsync();
        var actualAfter = await ActualNumCopies(request).SingleAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(boxBefore, boxAfter);
        Assert.Equal(actualBefore, actualAfter);
    }


    [Fact]
    public async Task OnPost_MixedWantGives_AppliesChanges()
    {
        var (want, give) = await _testGen.GetMixedRequestDeckAsync();
        var deckOwnerId = await OwnerId(want).SingleAsync();

        await _exchangeModel.SetModelContextAsync(_userManager, deckOwnerId);

        var wantTarget = want.NumCopies;
        var giveTarget = give.NumCopies;

        var actualWantBefore = await ActualNumCopies(want).SingleOrDefaultAsync();
        var actualGiveBefore = await ActualNumCopies(give).SingleAsync();

        var result = await _exchangeModel.OnPostAsync(want.LocationId, default);

        var actualWantAfter = await ActualNumCopies(want).SingleAsync();
        var actualGiveAfter = await ActualNumCopies(give).SingleOrDefaultAsync();

        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(want.LocationId, give.LocationId);
        Assert.Equal(wantTarget, actualWantAfter - actualWantBefore);
        Assert.Equal(giveTarget, actualGiveBefore - actualGiveAfter);
    }
}
