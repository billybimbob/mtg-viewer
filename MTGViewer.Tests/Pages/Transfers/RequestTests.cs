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
using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages.Transfers
{
    public class RequestTests : IAsyncLifetime
    {
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;
        private readonly TestDataGenerator _testGen;

        private readonly RequestModel _requestModel;
        private Deck _requestDeck;

        public RequestTests(
            CardDbContext dbContext,
            UserManager<CardUser> userManager,
            TestDataGenerator testGen)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _testGen = testGen;

            _requestModel = new(
                _dbContext, _userManager, Mock.Of<ILogger<RequestModel>>());
        }


        public async Task InitializeAsync()
        {
            await _testGen.SeedAsync();
            _requestDeck = await _testGen.CreateRequestDeckAsync();
        }

        public Task DisposeAsync() => _testGen.ClearAsync();


        private IQueryable<Trade> AllTrades =>
            _dbContext.Trades
                .AsNoTracking()
                .OrderBy(t => t.Id);


        [Fact]
        public async Task OnPost_WrongUser_NoChange()
        {
            // Arrange
            var wrongUser = await _dbContext.Users.FirstAsync(u => u.Id != _requestDeck.OwnerId);
            await _requestModel.SetModelContextAsync(_userManager, wrongUser.Id);

            var allTradeIds = AllTrades.Select(t => t.Id);

            // Act
            var tradesBefore = await allTradeIds.ToListAsync();
            var result = await _requestModel.OnPostAsync(_requestDeck.Id);
            var tradesAfter = await allTradeIds.ToListAsync();

            // // Assert
            Assert.IsType<NotFoundResult>(result);
            Assert.Equal(tradesBefore, tradesAfter);
        }


        [Fact]
        public async Task OnPost_InvalidDeck_NoChange()
        {
            // Arrange
            await _requestModel.SetModelContextAsync(_userManager, _requestDeck.OwnerId);

            var allTradeIds = AllTrades.Select(t => t.Id);
            var wrongDeck = await _dbContext.Decks
                .AsNoTracking()
                .FirstAsync(d => d.OwnerId != _requestDeck.OwnerId);

            // Act
            var tradesBefore = await allTradeIds.ToListAsync();
            var result = await _requestModel.OnPostAsync(wrongDeck.Id);
            var tradesAfter = await allTradeIds.ToListAsync();

            // // Assert
            Assert.IsType<NotFoundResult>(result);
            Assert.Equal(tradesBefore, tradesAfter);
        }


        [Fact]
        public async Task OnPost_ValidDeck_Requests()
        {
            // Arrange
            await _requestModel.SetModelContextAsync(_userManager, _requestDeck.OwnerId);

            // Act
            var tradesBefore = await AllTrades.Select(t => t.Id).ToListAsync();

            var result = await _requestModel.OnPostAsync(_requestDeck.Id);
            var tradesAfter = await AllTrades.ToListAsync();

            var addedTrades = tradesAfter.ExceptBy(tradesBefore, t => t.Id);

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

            var requestCard = await _dbContext.Wants
                .Where(w => w.LocationId == _requestDeck.Id)
                .Select(w => w.Card)
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
                .Select(loc => new Amount
                {
                    Card = requestCard,
                    Location = loc,
                    NumCopies = 2
                })
                .ToList();

            _dbContext.Decks.AttachRange(extraLocations);
            _dbContext.Amounts.AttachRange(amounts);

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
            
            // Act
            var tradesBefore = await AllTrades.Select(t => t.Id).ToListAsync();

            var result = await _requestModel.OnPostAsync(_requestDeck.Id);
            var tradesAfter = await AllTrades.ToListAsync();

            var addedTrades = tradesAfter.ExceptBy(tradesBefore, t => t.Id);
            var addedTargets = addedTrades.Select(t => t.FromId);

            // // Assert
            Assert.IsType<RedirectToPageResult>(result);

            Assert.All(addedTrades, t => Assert.Equal(_requestDeck.Id, t.ToId));
            Assert.All(extraLocations, l => Assert.Contains(l.Id, addedTargets));
        }
    }
}