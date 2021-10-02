using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        private readonly ServiceProvider _services;
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        private readonly CheckoutModel _checkoutModel;


        public CheckoutTests()
        {
            _services = TestFactory.ServiceProvider();
            _dbContext = TestFactory.CardDbContext(_services);
            _userManager = TestFactory.CardUserManager(_services);

            var sharedStorage = new ExpandableSharedService(
                Mock.Of<IConfiguration>(),
                _dbContext);

            _checkoutModel = new(
                _dbContext, sharedStorage, _userManager,
                Mock.Of<ILogger<CheckoutModel>>());
        }


        public Task InitializeAsync()
        {
            return _dbContext.SeedAsync(_userManager);
        }


        public async Task DisposeAsync()
        {
            await _services.DisposeAsync();
            await _dbContext.DisposeAsync();
            _userManager.Dispose();
        }


        private IQueryable<string> RequestOwnerId(CardRequest request) =>
            _dbContext.Decks
                .Where(d => d.Id == request.TargetId)
                .Select(d => d.OwnerId);


        private IQueryable<int> RequestAmount(CardRequest request) =>
            _dbContext.Requests
                .Where(cr => cr.Id == request.Id)
                .Select(cr => cr.Amount);


        private IQueryable<int> ActualAmount(CardRequest request) =>
            _dbContext.Amounts
                .Where(ca => ca.LocationId == request.TargetId && ca.CardId == request.CardId)
                .Select(ca => ca.Amount);


        private IQueryable<int> BoxAmount(CardRequest request) =>
            _dbContext.Amounts
                .Where(ca => ca.Location is Box && ca.CardId == request.CardId)
                .Select(ca => ca.Amount);


        private IQueryable<int> ChangeAmount(CardRequest request) =>
            _dbContext.Changes
                .Where(c => c.ToId == request.TargetId || c.FromId == request.TargetId)
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
            var request = await _dbContext.GetTakeRequestAsync();
            var targetAmount = request.Amount;
            var deckOwnerId = await RequestOwnerId(request).SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            // Act
            var takeAmountBefore = await RequestAmount(request).SingleAsync();
            var actualAmountBefore = await ActualAmount(request).SingleOrDefaultAsync();

            var boxTakeBefore = await BoxAmount(request).SumAsync();
            var changeBefore = await ChangeAmount(request).SumAsync();

            var result = await _checkoutModel.OnPostAsync(request.TargetId);

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
            var request = await _dbContext.GetTakeRequestAsync(targetMod);

            var targetLimit = request.Amount - targetMod;
            var deckOwnerId = await RequestOwnerId(request).SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            // Act
            var takeAmountBefore = await RequestAmount(request).SingleAsync();
            var actualAmountBefore = await ActualAmount(request).SingleOrDefaultAsync();

            var boxTakeBefore = await BoxAmount(request).SumAsync();
            var changeBefore = await ChangeAmount(request).SumAsync();

            var result = await _checkoutModel.OnPostAsync(request.TargetId);

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
            var request = await _dbContext.GetReturnRequestAsync(targetMod);
            var returnAmount = request.Amount;
            var deckOwnerId = await RequestOwnerId(request).SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            // Act
            var returnAmountBefore = await RequestAmount(request).SingleAsync();
            var actualAmountBefore = await ActualAmount(request).SingleAsync();

            var boxTakeBefore = await BoxAmount(request).SumAsync();
            var changeBefore = await ChangeAmount(request).SumAsync();

            var result = await _checkoutModel.OnPostAsync(request.TargetId);

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
            var request = await _dbContext.GetReturnRequestAsync(2);
            var deckOwnerId = await RequestOwnerId(request).SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            // Act
            var returnAmountBefore = await RequestAmount(request).SingleAsync();
            var actualAmountBefore = await ActualAmount(request).SingleAsync();

            var boxTakeBefore = await BoxAmount(request).SumAsync();
            var changeBefore = await ChangeAmount(request).SumAsync();

            var result = await _checkoutModel.OnPostAsync(request.TargetId);

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
            var request = await _dbContext.GetReturnRequestAsync(2);
            var deckOwnerId = await RequestOwnerId(request).SingleAsync();

            var tradeTarget = await _dbContext.Amounts
                .Where(ca => ca.Location is Deck
                    && (ca.Location as Deck).OwnerId != deckOwnerId)
                .Select(ca => ca.Location)
                .FirstAsync();

            var activeTrade = new Trade
            {
                Card = request.Card,
                To = request.Target,
                From = (Deck)tradeTarget,
                Amount = 3
            };

            _dbContext.Trades.Attach(activeTrade);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            var boxBefore = await BoxAmount(request).SumAsync();
            var actualBefore = await ActualAmount(request).SingleAsync();

            var result = await _checkoutModel.OnPostAsync(request.TargetId);

            var boxAfter = await BoxAmount(request).SumAsync();
            var actualAfter = await ActualAmount(request).SingleAsync();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(boxBefore, boxAfter);
            Assert.Equal(actualBefore, actualAfter);
        }


        [Fact]
        public async Task OnPost_MixedTakeReturns_AppliesChanges()
        {
            var (take, ret) = await _dbContext.GetMixedRequestDeckAsync();
            var deckOwnerId = await RequestOwnerId(take).SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            var takeTarget = take.Amount;
            var retTarget = ret.Amount;

            var actualTakeBefore = await ActualAmount(take).SingleOrDefaultAsync();
            var actualRetBefore = await ActualAmount(ret).SingleAsync();

            var result = await _checkoutModel.OnPostAsync(take.TargetId);

            var actualTakeAfter = await ActualAmount(take).SingleAsync();
            var actualRetAfter = await ActualAmount(ret).SingleOrDefaultAsync();

            Assert.IsType<RedirectToPageResult>(result);

            Assert.Equal(take.TargetId, ret.TargetId);
            Assert.Equal(takeTarget, actualTakeAfter - actualTakeBefore);
            Assert.Equal(retTarget, actualRetBefore - actualRetAfter);
        }
    }
}