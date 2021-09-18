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
            var takeTarget = await _dbContext.BoxAmounts
                .Include(ba => ba.Card)
                .AsNoTracking()
                .FirstAsync(ba => ba.Amount > 0);

            var targetAmount = takeTarget.Amount;

            var deckTarget = await _dbContext.Decks
                .AsNoTracking()
                .FirstAsync();

            await _checkoutModel.SetModelContextAsync(_userManager, deckTarget.OwnerId);

            var deckTakeQuery = _dbContext.DeckAmounts
                .Where(da => da.LocationId == deckTarget.Id
                    && da.CardId == takeTarget.CardId
                    && da.Intent == Intent.Take);

            var deckTake = await deckTakeQuery.SingleOrDefaultAsync();

            if (deckTake == default)
            {
                deckTake = new()
                {
                    Card = takeTarget.Card,
                    Location = deckTarget,
                    Intent = Intent.Take
                };

                _dbContext.DeckAmounts.Attach(deckTake);
            }

            deckTake.Amount = targetAmount;

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            var takeAmountQuery = deckTakeQuery.Select(da => da.Amount);

            var actualAmountQuery = _dbContext.DeckAmounts
                .Where(da => da.LocationId == deckTarget.Id
                    && da.CardId == takeTarget.CardId
                    && da.Intent == Intent.None)
                .Select(da => da.Amount);

            var boxAmountQuery = _dbContext.BoxAmounts
                .Where(ba => ba.Id == takeTarget.Id)
                .Select(ba => ba.Amount);

            // Act
            var takeAmountBefore = await takeAmountQuery.SingleAsync();
            var actualAmountBefore = await actualAmountQuery.SingleOrDefaultAsync();
            var boxTakeBefore = await boxAmountQuery.SingleAsync();

            var result = await _checkoutModel.OnPostAsync(deckTarget.Id);

            var takeAmountAfter = await takeAmountQuery.SingleOrDefaultAsync();
            var actualAmountAfter = await actualAmountQuery.SingleAsync();
            var boxTakeAfter = await boxAmountQuery.SingleAsync();

            // Assert
            Assert.IsType<PageResult>(result);

            Assert.Equal(targetAmount, takeAmountBefore - takeAmountAfter);
            Assert.Equal(targetAmount, actualAmountAfter - actualAmountBefore);
            Assert.Equal(targetAmount, boxTakeBefore - boxTakeAfter);
        }


        [Fact]
        public async Task OnPost_InsufficientTake_TakeLowered()
        {
        }


        [Fact]
        public async Task OnPost_ValidReturn_AppliesReturn()
        {
        }


        [Fact]
        public async Task OnPost_InsufficientReturn_NoChange()
        {
        }


        [Fact]
        public async Task OnPost_MixedTakeReturns_AppliesChanges()
        {
        }
    }
}