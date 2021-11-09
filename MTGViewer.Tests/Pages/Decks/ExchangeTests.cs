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
        ITreasury treasury,
        UserManager<CardUser> userManager,
        CardText cardText,
        TestDataGenerator testGen)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _testGen = testGen;

        var logger = Mock.Of<ILogger<ExchangeModel>>();

        _exchangeModel = new(
            _dbContext, treasury, _userManager, cardText, logger);
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

        var result = await _exchangeModel.OnPostAsync(invalidDeckId);

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

        var result = await _exchangeModel.OnPostAsync(deck.Id);

        Assert.IsType<NotFoundResult>(result);
    }


    [Fact]
    public async Task OnPost_ValidTake_AppliesTake()
    {
        // Arrange
        var want = await _testGen.GetWantAsync();
        var targetAmount = want.NumCopies;
        var deckOwnerId = await OwnerId(want).SingleAsync();

        await _exchangeModel.SetModelContextAsync(_userManager, deckOwnerId);

        // Act
        var takeAmountBefore = await NumCopies(want).SingleAsync();
        var actualAmountBefore = await ActualNumCopies(want).SingleOrDefaultAsync();

        var boxTakeBefore = await BoxNumCopies(want).SumAsync();
        var changeBefore = await ChangeAmount(want).SumAsync();

        var result = await _exchangeModel.OnPostAsync(want.LocationId);

        var takeAmountAfter = await NumCopies(want).SingleOrDefaultAsync();
        var actualAmountAfter = await ActualNumCopies(want).SingleAsync();

        var boxTakeAfter = await BoxNumCopies(want).SumAsync();
        var changeAfter = await ChangeAmount(want).SumAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(targetAmount, takeAmountBefore - takeAmountAfter);
        Assert.Equal(targetAmount, actualAmountAfter - actualAmountBefore);

        Assert.Equal(targetAmount, boxTakeBefore - boxTakeAfter);
        Assert.Equal(targetAmount, changeAfter - changeBefore);
    }


    [Fact]
    public async Task OnPost_InsufficientTake_TakeLowered()
    {
        // Arrange
        var targetMod = 2;
        var request = await _testGen.GetWantAsync(targetMod);

        var targetLimit = request.NumCopies - targetMod;
        var deckOwnerId = await OwnerId(request).SingleAsync();

        await _exchangeModel.SetModelContextAsync(_userManager, deckOwnerId);

        // Act
        var takeAmountBefore = await NumCopies(request).SingleAsync();
        var actualAmountBefore = await ActualNumCopies((Quantity)request).SingleOrDefaultAsync();

        var boxTakeBefore = await BoxNumCopies(request).SumAsync();
        var changeBefore = await ChangeAmount(request).SumAsync();

        var result = await _exchangeModel.OnPostAsync(request.LocationId);

        var takeAmountAfter = await NumCopies(request).SingleAsync();
        var actualAmountAfter = await ActualNumCopies((Quantity)request).SingleAsync();

        var boxTakeAfter = await BoxNumCopies(request).SumAsync();
        var changeAfter = await ChangeAmount(request).SumAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(targetLimit, takeAmountBefore - takeAmountAfter);
        Assert.Equal(targetLimit, actualAmountAfter - actualAmountBefore);

        Assert.Equal(targetLimit, boxTakeBefore - boxTakeAfter);
        Assert.Equal(targetLimit, changeAfter - changeBefore);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task OnPost_ValidReturn_AppliesReturn(int targetMod)
    {
        // Arrange
        var request = await _testGen.GetGiveBackAsync(targetMod);
        var returnAmount = request.NumCopies;
        var deckOwnerId = await OwnerId(request).SingleAsync();

        await _exchangeModel.SetModelContextAsync(_userManager, deckOwnerId);

        // Act
        var returnAmountBefore = await NumCopies(request).SingleAsync();
        var actualAmountBefore = await ActualNumCopies((Quantity)request).SingleAsync();

        var boxTakeBefore = await BoxNumCopies(request).SumAsync();
        var changeBefore = await ChangeAmount(request).SumAsync();

        var result = await _exchangeModel.OnPostAsync(request.LocationId);

        var returnAmountAfter = await NumCopies(request).SingleOrDefaultAsync();
        var actualAmountAfter = await ActualNumCopies((Quantity)request).SingleOrDefaultAsync();

        var boxTakeAfter = await BoxNumCopies(request).SumAsync();
        var changeAfter = await ChangeAmount(request).SumAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(returnAmount, returnAmountBefore - returnAmountAfter);
        Assert.Equal(returnAmount, actualAmountBefore - actualAmountAfter);

        Assert.Equal(returnAmount, boxTakeAfter - boxTakeBefore);
        Assert.Equal(returnAmount, changeAfter - changeBefore);
    }


    [Fact]
    public async Task OnPost_InsufficientReturn_NoChange()
    {
        // Arrange
        var request = await _testGen.GetGiveBackAsync(2);
        var deckOwnerId = await OwnerId(request).SingleAsync();

        await _exchangeModel.SetModelContextAsync(_userManager, deckOwnerId);

        // Act
        var returnAmountBefore = await NumCopies(request).SingleAsync();
        var actualAmountBefore = await ActualNumCopies((Quantity)request).SingleAsync();

        var boxTakeBefore = await BoxNumCopies(request).SumAsync();
        var changeBefore = await ChangeAmount(request).SumAsync();

        var result = await _exchangeModel.OnPostAsync(request.LocationId);

        var returnAmountAfter = await NumCopies(request).SingleAsync();
        var actualAmountAfter = await ActualNumCopies((Quantity)request).SingleAsync();

        var boxTakeAfter = await BoxNumCopies(request).SumAsync();
        var changeAfter = await ChangeAmount(request).SumAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(returnAmountBefore, returnAmountAfter);
        Assert.Equal(actualAmountBefore, actualAmountAfter);

        Assert.Equal(boxTakeBefore, boxTakeAfter);
        Assert.Equal(changeBefore, changeAfter);
    }


    [Fact]
    public async Task OnPost_TradeActive_NoChange()
    {
        var request = await _testGen.GetGiveBackAsync(2);
        var deckOwnerId = await OwnerId(request).SingleAsync();

        var tradeTarget = await _dbContext.Amounts
            .Where(ca => ca.Location is Deck
                && (ca.Location as Deck).OwnerId != deckOwnerId)
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
        var actualBefore = await ActualNumCopies((Quantity)request).SingleAsync();

        var result = await _exchangeModel.OnPostAsync(request.LocationId);

        var boxAfter = await BoxNumCopies(request).SumAsync();
        var actualAfter = await ActualNumCopies((Quantity)request).SingleAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(boxBefore, boxAfter);
        Assert.Equal(actualBefore, actualAfter);
    }


    [Fact]
    public async Task OnPost_MixedTakeReturns_AppliesChanges()
    {
        var (take, ret) = await _testGen.GetMixedRequestDeckAsync();
        var deckOwnerId = await OwnerId(take).SingleAsync();

        await _exchangeModel.SetModelContextAsync(_userManager, deckOwnerId);

        var takeTarget = take.NumCopies;
        var retTarget = ret.NumCopies;

        var actualTakeBefore = await ActualNumCopies((Quantity)take).SingleOrDefaultAsync();
        var actualRetBefore = await ActualNumCopies((Quantity)ret).SingleAsync();

        var result = await _exchangeModel.OnPostAsync(take.LocationId);

        var actualTakeAfter = await ActualNumCopies((Quantity)take).SingleAsync();
        var actualRetAfter = await ActualNumCopies((Quantity)ret).SingleOrDefaultAsync();

        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(take.LocationId, ret.LocationId);
        Assert.Equal(takeTarget, actualTakeAfter - actualTakeBefore);
        Assert.Equal(retTarget, actualRetBefore - actualRetAfter);
    }
}