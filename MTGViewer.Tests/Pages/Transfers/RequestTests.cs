using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages.Transfers
{
    public class RequestTests : IAsyncLifetime
    {
        private readonly ServiceProvider _services;
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        private readonly RequestModel _requestModel;

        public RequestTests()
        {
            _services = TestFactory.ServiceProvider();
            _dbContext = TestFactory.CardDbContext(_services);
            _userManager = TestFactory.CardUserManager(_services);

            _requestModel = new RequestModel(
                _dbContext, _userManager, Mock.Of<ILogger<RequestModel>>());
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
        public async Task OnPost_WrongUser_NoChange()
        {
            // Arrange
            var deck = await _dbContext.CreateRequestDeckAsync();

            var wrongUser = await _dbContext.Users.FirstAsync(u => u.Id != deck.Owner.Id);
            var tradesQuery = _dbContext.Trades.AsNoTracking();

            await _requestModel.SetModelContextAsync(_userManager, wrongUser.Id);

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await _requestModel.OnPostAsync(deck.Id);
            var tradesAfter = await tradesQuery.ToListAsync();

            // // Assert
            Assert.IsType<NotFoundResult>(result);
            Assert.Equal(tradesBefore.Select(t => t.Id), tradesAfter.Select(t => t.Id));
            Assert.DoesNotContain(deck.Id, tradesAfter.Select(t => t.ToId));
        }


        [Fact]
        public async Task OnPost_InvalidDeck_NoChange()
        {
            // Arrange
            var deck = await _dbContext.CreateRequestDeckAsync();

            var tradesQuery = _dbContext.Trades.AsNoTracking();
            var wrongDeck = _dbContext.Decks
                .AsNoTracking()
                .FirstAsync(t => t.Id != deck.Id);

            await _requestModel.SetModelContextAsync(_userManager, deck.Owner.Id);

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await _requestModel.OnPostAsync(wrongDeck.Id);
            var tradesAfter = await tradesQuery.ToListAsync();

            // // Assert
            Assert.IsType<NotFoundResult>(result);
            Assert.Equal(tradesBefore.Select(t => t.Id), tradesAfter.Select(t => t.Id));
            Assert.DoesNotContain(deck.Id, tradesAfter.Select(t => t.ToId));
        }


        [Fact]
        public async Task OnPost_ValidDeck_Requests()
        {
            // Arrange
            var deck = await _dbContext.CreateRequestDeckAsync();
            var tradesQuery = _dbContext.Trades.AsNoTracking();

            await _requestModel.SetModelContextAsync(_userManager, deck.Owner.Id);

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await _requestModel.OnPostAsync(deck.Id);

            var tradesAfter = await tradesQuery.ToListAsync();
            var addedTrades = tradesAfter.Except(
                tradesBefore,
                new EntityComparer<Trade>(t => t.Id));

            // // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotEqual(tradesBefore, tradesAfter);
            Assert.All(addedTrades, t => Assert.Equal(deck.Id, t.ToId));
        }


        [Fact]
        public async Task OnPost_MultipleSources_RequestsAll()
        {
            // Arrange
            var deck = await _dbContext.CreateRequestDeckAsync();
            var tradesQuery = _dbContext.Trades.AsNoTracking();

            await _requestModel.SetModelContextAsync(_userManager, deck.Owner.Id);

            var requestCard = await _dbContext.Amounts
                .Where(ca => ca.LocationId == deck.Id && ca.IsRequest)
                .Select(ca => ca.Card)
                .AsNoTracking()
                .FirstAsync();

            var nonOwner = await _dbContext.Users
                .FirstAsync(u => u.Id != deck.OwnerId);

            var extraLocations = Enumerable
                .Range(0, 3)
                .Select(i => new Deck($"Extra Deck #{i}")
                {
                    Owner = nonOwner
                })
                .ToList();

            var amounts = extraLocations
                .Select(loc => new CardAmount
                {
                    Card = requestCard,
                    Location = loc,
                    Amount = 2
                })
                .ToList();

            _dbContext.Decks.AttachRange(extraLocations);
            _dbContext.Amounts.AttachRange(amounts);

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await _requestModel.OnPostAsync(deck.Id);

            var tradesAfter = await tradesQuery.ToListAsync();
            var addedTrades = tradesAfter.Except(
                tradesBefore,
                new EntityComparer<Trade>(t => t.Id));

            var addedTargets = addedTrades.Select(t => t.FromId);

            // // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotEqual(tradesBefore, tradesAfter);

            Assert.All(addedTrades, t => Assert.Equal(deck.Id, t.ToId));
            Assert.All(extraLocations, l => Assert.Contains(l.Id, addedTargets));
        }
    }
}