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


namespace MTGViewer.Tests.Pages.Decks
{
    public class CheckoutTests : IAsyncLifetime
    {
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;
        private readonly TestDataGenerator _testGen;

        private readonly CheckoutModel _checkoutModel;


        public CheckoutTests(
            CardDbContext dbContext,
            ISharedStorage sharedStorage,
            UserManager<CardUser> userManager,
            IMTGSymbols iconMarkup,
            TestDataGenerator testGen)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _testGen = testGen;

            var logger = Mock.Of<ILogger<CheckoutModel>>();

            _checkoutModel = new(
                _dbContext, sharedStorage, _userManager, iconMarkup, logger);
        }


        public Task InitializeAsync() => _testGen.SeedAsync();

        public Task DisposeAsync() => _testGen.ClearAsync();


        private IQueryable<string> RequestOwnerId(CardRequest request) =>
            _dbContext.Decks
                .Where(d => d.Id == request.DeckId)
                .Select(d => d.OwnerId);


        private IQueryable<int> RequestAmount(Want request) =>
            _dbContext.Wants
                .Where(w => w.Id == request.Id)
                .Select(w => w.Amount);


        private IQueryable<int> RequestAmount(GiveBack request) =>
            _dbContext.GiveBacks
                .Where(g => g.Id == request.Id)
                .Select(g => g.Amount);


        private IQueryable<int> ActualAmount(CardRequest request) =>
            _dbContext.Amounts
                .Where(ca => ca.LocationId == request.DeckId && ca.CardId == request.CardId)
                .Select(ca => ca.Amount);


        private IQueryable<int> BoxAmount(CardRequest request) =>
            _dbContext.Amounts
                .Where(ca => ca.Location is Box && ca.CardId == request.CardId)
                .Select(ca => ca.Amount);


        private IQueryable<int> ChangeAmount(CardRequest request) =>
            _dbContext.Changes
                .Where(c => c.ToId == request.DeckId || c.FromId == request.DeckId)
                .Select(c => c.Amount);



        [Fact]
        public async Task OnPost_InvalidDeck_NotFound()
        {
            var invalidDeckId = 0;
            var validUserId = await _dbContext.Users.Select(u => u.Id).FirstAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, validUserId);

            var result = await _checkoutModel.OnPostAsync(invalidDeckId);

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

            await _checkoutModel.SetModelContextAsync(_userManager, invalidUserId);

            var result = await _checkoutModel.OnPostAsync(deck.Id);

            Assert.IsType<NotFoundResult>(result);
        }


        [Fact]
        public async Task OnPost_ValidTake_AppliesTake()
        {
            // Arrange
            var request = await _testGen.GetWantAsync();
            var targetAmount = request.Amount;
            var deckOwnerId = await RequestOwnerId(request).SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            // Act
            var takeAmountBefore = await RequestAmount(request).SingleAsync();
            var actualAmountBefore = await ActualAmount(request).SingleOrDefaultAsync();

            var boxTakeBefore = await BoxAmount(request).SumAsync();
            var changeBefore = await ChangeAmount(request).SumAsync();

            var result = await _checkoutModel.OnPostAsync(request.DeckId);

            var takeAmountAfter = await RequestAmount(request).SingleOrDefaultAsync();
            var actualAmountAfter = await ActualAmount(request).SingleAsync();

            var boxTakeAfter = await BoxAmount(request).SumAsync();
            var changeAfter = await ChangeAmount(request).SumAsync();

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

            var targetLimit = request.Amount - targetMod;
            var deckOwnerId = await RequestOwnerId(request).SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            // Act
            var takeAmountBefore = await RequestAmount(request).SingleAsync();
            var actualAmountBefore = await ActualAmount(request).SingleOrDefaultAsync();

            var boxTakeBefore = await BoxAmount(request).SumAsync();
            var changeBefore = await ChangeAmount(request).SumAsync();

            var result = await _checkoutModel.OnPostAsync(request.DeckId);

            var takeAmountAfter = await RequestAmount(request).SingleAsync();
            var actualAmountAfter = await ActualAmount(request).SingleAsync();

            var boxTakeAfter = await BoxAmount(request).SumAsync();
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
            var returnAmount = request.Amount;
            var deckOwnerId = await RequestOwnerId(request).SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            // Act
            var returnAmountBefore = await RequestAmount(request).SingleAsync();
            var actualAmountBefore = await ActualAmount(request).SingleAsync();

            var boxTakeBefore = await BoxAmount(request).SumAsync();
            var changeBefore = await ChangeAmount(request).SumAsync();

            var result = await _checkoutModel.OnPostAsync(request.DeckId);

            var returnAmountAfter = await RequestAmount(request).SingleOrDefaultAsync();
            var actualAmountAfter = await ActualAmount(request).SingleOrDefaultAsync();

            var boxTakeAfter = await BoxAmount(request).SumAsync();
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
            var deckOwnerId = await RequestOwnerId(request).SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            // Act
            var returnAmountBefore = await RequestAmount(request).SingleAsync();
            var actualAmountBefore = await ActualAmount(request).SingleAsync();

            var boxTakeBefore = await BoxAmount(request).SumAsync();
            var changeBefore = await ChangeAmount(request).SumAsync();

            var result = await _checkoutModel.OnPostAsync(request.DeckId);

            var returnAmountAfter = await RequestAmount(request).SingleAsync();
            var actualAmountAfter = await ActualAmount(request).SingleAsync();

            var boxTakeAfter = await BoxAmount(request).SumAsync();
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
            var deckOwnerId = await RequestOwnerId(request).SingleAsync();

            var tradeTarget = await _dbContext.Amounts
                .Where(ca => ca.Location is Deck
                    && (ca.Location as Deck).OwnerId != deckOwnerId)
                .Select(ca => ca.Location)
                .FirstAsync();

            var activeTrade = new Trade
            {
                Card = request.Card,
                To = request.Deck,
                From = (Deck)tradeTarget,
                Amount = 3
            };

            _dbContext.Trades.Attach(activeTrade);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            var boxBefore = await BoxAmount(request).SumAsync();
            var actualBefore = await ActualAmount(request).SingleAsync();

            var result = await _checkoutModel.OnPostAsync(request.DeckId);

            var boxAfter = await BoxAmount(request).SumAsync();
            var actualAfter = await ActualAmount(request).SingleAsync();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(boxBefore, boxAfter);
            Assert.Equal(actualBefore, actualAfter);
        }


        [Fact]
        public async Task OnPost_MixedTakeReturns_AppliesChanges()
        {
            var (take, ret) = await _testGen.GetMixedRequestDeckAsync();
            var deckOwnerId = await RequestOwnerId(take).SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            var takeTarget = take.Amount;
            var retTarget = ret.Amount;

            var actualTakeBefore = await ActualAmount(take).SingleOrDefaultAsync();
            var actualRetBefore = await ActualAmount(ret).SingleAsync();

            var result = await _checkoutModel.OnPostAsync(take.DeckId);

            var actualTakeAfter = await ActualAmount(take).SingleAsync();
            var actualRetAfter = await ActualAmount(ret).SingleOrDefaultAsync();

            Assert.IsType<RedirectToPageResult>(result);

            Assert.Equal(take.DeckId, ret.DeckId);
            Assert.Equal(takeTarget, actualTakeAfter - actualTakeBefore);
            Assert.Equal(retTarget, actualRetBefore - actualRetAfter);
        }
    }
}