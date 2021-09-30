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
        private Deck _requestDeck;

        public RequestTests()
        {
            _services = TestFactory.ServiceProvider();
            _dbContext = TestFactory.CardDbContext(_services);
            _userManager = TestFactory.CardUserManager(_services);

            _requestModel = new(
                _dbContext, _userManager, Mock.Of<ILogger<RequestModel>>());
        }


        public async Task InitializeAsync()
        {
            await _dbContext.SeedAsync(_userManager);
            _requestDeck = await _dbContext.CreateRequestDeckAsync();
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
            var wrongUser = await _dbContext.Users.FirstAsync(u => u.Id != _requestDeck.OwnerId);
            await _requestModel.SetModelContextAsync(_userManager, wrongUser.Id);

            var tradeIdsQuery = _dbContext.Trades
                .OrderBy(t => t.Id)
                .Select(t => t.Id);

            // Act
            var tradesBefore = await tradeIdsQuery.ToListAsync();
            var result = await _requestModel.OnPostAsync(_requestDeck.Id);
            var tradesAfter = await tradeIdsQuery.ToListAsync();

            // // Assert
            Assert.IsType<NotFoundResult>(result);
            Assert.Equal(tradesBefore, tradesAfter);
        }


        [Fact]
        public async Task OnPost_InvalidDeck_NoChange()
        {
            // Arrange
            await _requestModel.SetModelContextAsync(_userManager, _requestDeck.OwnerId);

            var tradeIdsQuery = _dbContext.Trades
                .OrderBy(t => t.Id)
                .Select(t => t.Id);

            var wrongDeck = await _dbContext.Decks
                .AsNoTracking()
                .FirstAsync(d => d.OwnerId != _requestDeck.OwnerId);

            // Act
            var tradesBefore = await tradeIdsQuery.ToListAsync();
            var result = await _requestModel.OnPostAsync(wrongDeck.Id);
            var tradesAfter = await tradeIdsQuery.ToListAsync();

            // // Assert
            Assert.IsType<NotFoundResult>(result);
            Assert.Equal(tradesBefore, tradesAfter);
        }


        [Fact]
        public async Task OnPost_ValidDeck_Requests()
        {
            // Arrange
            await _requestModel.SetModelContextAsync(_userManager, _requestDeck.OwnerId);
            var tradesQuery = _dbContext.Trades.AsNoTracking();

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await _requestModel.OnPostAsync(_requestDeck.Id);
            var tradesAfter = await tradesQuery.ToListAsync();

            var addedTrades = tradesAfter
                .GroupJoin( tradesBefore,
                    ta => ta.Id, tb => tb.Id,
                    (trade, tbs) => (trade, isNew: !tbs.Any()))
                .Where(tn => tn.isNew)
                .Select(tn => tn.trade);

            // // Assert
            Assert.IsType<RedirectToPageResult>(result);

            Assert.NotEmpty(addedTrades);
            Assert.All(addedTrades, t => Assert.Equal(_requestDeck.Id, t.ToId));
        }


        [Fact]
        public async Task OnPost_MultipleSources_RequestsAll()
        {
            // Arrange
            await _requestModel.SetModelContextAsync(_userManager, _requestDeck.OwnerId);

            var requestCard = await _dbContext.Requests
                .Where(ex => ex.TargetId == _requestDeck.Id)
                .Select(ex => ex.Card)
                .AsNoTracking()
                .FirstAsync();

            var nonOwner = await _dbContext.Users
                .FirstAsync(u => u.Id != _requestDeck.OwnerId);

            var extraLocations = Enumerable
                .Range(0, 3)
                .Select(i => new Deck
                {
                    Name = $"Extra Deck #{i}",
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
            
            var tradesQuery = _dbContext.Trades.AsNoTracking();

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await _requestModel.OnPostAsync(_requestDeck.Id);
            var tradesAfter = await tradesQuery.ToListAsync();

            var addedTrades = tradesAfter
                .GroupJoin( tradesBefore,
                    ta => ta.Id, tb => tb.Id,
                    (trade, tbs) => (trade, isNew: !tbs.Any()))
                .Where(tn => tn.isNew)
                .Select(tn => tn.trade);

            var addedTargets = addedTrades.Select(t => t.FromId);

            // // Assert
            Assert.IsType<RedirectToPageResult>(result);

            Assert.All(addedTrades, t => Assert.Equal(_requestDeck.Id, t.ToId));
            Assert.All(extraLocations, l => Assert.Contains(l.Id, addedTargets));
        }
    }
}