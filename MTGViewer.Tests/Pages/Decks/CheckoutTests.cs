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


        public async Task InitializeAsync()
        {
            await _dbContext.SeedAsync(_userManager);
        }


        public async Task DisposeAsync()
        {
            await _services.DisposeAsync();
            await _dbContext.DisposeAsync();
            _userManager.Dispose();
        }



        [Fact]
        public async Task OnPost_InvalidDeck_NotFound()
        {
            var validUserId = await _dbContext.Users.Select(u => u.Id).FirstAsync();
            await _checkoutModel.SetModelContextAsync(_userManager, validUserId);

            var invalidDeckId = -1;
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

            var deckOwnerId = await _dbContext.Decks
                .Where(d => d.Id == request.ToId)
                .Select(d => d.OwnerId)
                .SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            var takeAmountQuery = _dbContext.Amounts
                .Where(da => da.Id == request.Id)
                .Select(da => da.Amount);

            var actualAmountQuery = _dbContext.Amounts
                .Where(ca => ca.LocationId == request.ToId && ca.CardId == request.CardId)
                .Select(ca => ca.Amount);

            var boxAmountQuery = _dbContext.Amounts
                .Where(ca => ca.Location is Box && ca.CardId == request.CardId)
                .Select(ca => ca.Amount);

            var targetAmount = request.Amount;

            // Act
            var takeAmountBefore = await takeAmountQuery.SingleAsync();
            var actualAmountBefore = await actualAmountQuery.SingleOrDefaultAsync();
            var boxTakeBefore = await boxAmountQuery.SumAsync();

            var result = await _checkoutModel.OnPostAsync(request.ToId.Value);

            var takeAmountAfter = await takeAmountQuery.SingleOrDefaultAsync();
            var actualAmountAfter = await actualAmountQuery.SingleAsync();
            var boxTakeAfter = await boxAmountQuery.SumAsync();

            // Assert
            Assert.IsType<PageResult>(result);
            Assert.Equal(targetAmount, takeAmountBefore - takeAmountAfter);
            Assert.Equal(targetAmount, actualAmountAfter - actualAmountBefore);
            Assert.Equal(targetAmount, boxTakeBefore - boxTakeAfter);
        }


        [Fact]
        public async Task OnPost_InsufficientTake_TakeLowered()
        {
            // Arrange
            var targetMod = 2;
            var request = await _dbContext.GetTakeRequestAsync(targetMod);

            var deckOwnerId = await _dbContext.Decks
                .Where(d => d.Id == request.ToId)
                .Select(d => d.OwnerId)
                .SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            var takeRequestQuery = _dbContext.Exchanges
                .Where(ex => ex.Id == request.Id)
                .Select(ex => ex.Amount);

            var actualAmountQuery = _dbContext.Amounts
                .Where(ca => ca.LocationId == request.ToId && ca.CardId == request.CardId)
                .Select(ca => ca.Amount);

            var boxAmountQuery = _dbContext.Amounts
                .Where(ca => ca.Location is Box && ca.CardId == request.CardId)
                .Select(ca => ca.Amount);

            var targetLimit = request.Amount - targetMod;

            // Act
            var takeAmountBefore = await takeRequestQuery.SingleAsync();
            var actualAmountBefore = await actualAmountQuery.SingleOrDefaultAsync();
            var boxTakeBefore = await boxAmountQuery.SumAsync();

            var result = await _checkoutModel.OnPostAsync(request.ToId.Value);

            var takeAmountAfter = await takeRequestQuery.SingleAsync();
            var actualAmountAfter = await actualAmountQuery.SingleAsync();
            var boxTakeAfter = await boxAmountQuery.SumAsync();

            // Assert
            Assert.IsType<PageResult>(result);
            Assert.Equal(targetLimit, takeAmountBefore - takeAmountAfter);
            Assert.Equal(targetLimit, actualAmountAfter - actualAmountBefore);
            Assert.Equal(targetLimit, boxTakeBefore - boxTakeAfter);
        }


        [Theory]
        [InlineData(0)]
        [InlineData(-2)]
        public async Task OnPost_ValidReturn_AppliesReturn(int targetMod)
        {
            // Arrange
            var request = await _dbContext.GetReturnRequestAsync(targetMod);

            var deckOwnerId = await _dbContext.Decks
                .Where(d => d.Id == request.FromId)
                .Select(d => d.OwnerId)
                .SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            var returnRequestQuery = _dbContext.Exchanges
                .Where(ex => ex.Id == request.Id)
                .Select(ex => ex.Amount);

            var actualAmountQuery = _dbContext.Amounts
                .Where(ca => ca.LocationId == request.FromId && ca.CardId == request.CardId)
                .Select(ca => ca.Amount);

            var boxAmountQuery = _dbContext.Amounts
                .Where(ca => ca.Location is Box && ca.CardId == request.CardId)
                .Select(ca => ca.Amount);

            var returnAmount = request.Amount;

            // Act
            var returnAmountBefore = await returnRequestQuery.SingleAsync();
            var actualAmountBefore = await actualAmountQuery.SingleAsync();
            var boxTakeBefore = await boxAmountQuery.SumAsync();

            var result = await _checkoutModel.OnPostAsync(request.FromId.Value);

            var returnAmountAfter = await returnRequestQuery.SingleOrDefaultAsync();
            var actualAmountAfter = await actualAmountQuery.SingleOrDefaultAsync();
            var boxTakeAfter = await boxAmountQuery.SumAsync();

            // Assert
            Assert.IsType<PageResult>(result);
            Assert.Equal(returnAmount, returnAmountBefore - returnAmountAfter);
            Assert.Equal(returnAmount, actualAmountBefore - actualAmountAfter);
            Assert.Equal(returnAmount, boxTakeAfter - boxTakeBefore);
        }


        [Fact]
        public async Task OnPost_InsufficientReturn_NoChange()
        {
            // Arrange
            var request = await _dbContext.GetReturnRequestAsync(2);

            var deckOwnerId = await _dbContext.Decks
                .Where(d => d.Id == request.FromId)
                .Select(d => d.OwnerId)
                .SingleAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckOwnerId);

            var returnAmountQuery = _dbContext.Exchanges
                .Where(ex => ex.Id == request.Id)
                .Select(ex => ex.Amount);

            var actualAmountQuery = _dbContext.Amounts
                .Where(ca => ca.LocationId == request.FromId && ca.CardId == request.CardId)
                .Select(ca => ca.Amount);

            var boxAmountQuery = _dbContext.Amounts
                .Where(ca => ca.Location is Box && ca.CardId == request.CardId)
                .Select(ca => ca.Amount);

            // Act
            var returnAmountBefore = await returnAmountQuery.SingleAsync();
            var actualAmountBefore = await actualAmountQuery.SingleAsync();
            var boxTakeBefore = await boxAmountQuery.SumAsync();

            var result = await _checkoutModel.OnPostAsync(request.FromId.Value);

            var returnAmountAfter = await returnAmountQuery.SingleAsync();
            var actualAmountAfter = await actualAmountQuery.SingleAsync();
            var boxTakeAfter = await boxAmountQuery.SumAsync();

            // Assert
            Assert.IsType<PageResult>(result);
            Assert.Equal(returnAmountBefore, returnAmountAfter);
            Assert.Equal(actualAmountBefore, actualAmountAfter);
            Assert.Equal(boxTakeBefore, boxTakeAfter);
        }


        // [Fact]
        // public async Task OnPost_MixedTakeReturns_AppliesChanges()
        // {
        // }
    }
}