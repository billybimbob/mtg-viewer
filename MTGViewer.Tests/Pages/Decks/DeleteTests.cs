using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
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
    public class DeleteTests : IAsyncLifetime
    {
        private readonly ServiceProvider _services;
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        private readonly DeleteModel _deleteModel;
        private Deck _deck;


        public DeleteTests()
        {
            _services = TestFactory.ServiceProvider();
            _dbContext = TestFactory.CardDbContext(_services);
            _userManager = TestFactory.CardUserManager(_services);

            var sharedStorage = new ExpandableSharedService(
                Mock.Of<IConfiguration>(),
                _dbContext);

            _deleteModel = new DeleteModel(
                _userManager,
                _dbContext, 
                sharedStorage,
                Mock.Of<ILogger<DeleteModel>>());
        }


        public async Task InitializeAsync()
        {
            await _dbContext.SeedAsync(_userManager);
            _deck = await _dbContext.CreateDeckAsync();
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
            var wrongUser = await _dbContext.Users.FirstAsync(u => u.Id != _deck.OwnerId);

            await _deleteModel.SetModelContextAsync(_userManager, wrongUser.Id);

            var deckQuery = _dbContext.Decks
                .Where(d => d.Id == _deck.Id)
                .AsNoTracking();

            // Act
            var result = await _deleteModel.OnPostAsync(_deck.Id);
            var deckAfter = await deckQuery.SingleOrDefaultAsync();

            // // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(deckAfter);
        }


        [Fact]
        public async Task OnPost_InvalidDeck_NoChange()
        {
            // Arrange
            await _deleteModel.SetModelContextAsync(_userManager, _deck.OwnerId);

            var wrongDeck = -1;
            var deckQuery = _dbContext.Decks
                .Where(d => d.Id == _deck.Id)
                .AsNoTracking();

            // Act
            var result = await _deleteModel.OnPostAsync(wrongDeck);
            var deckAfter = await deckQuery.SingleOrDefaultAsync();

            // // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(deckAfter);
        }


        [Fact]
        public async Task OnPost_ValidDeck_ReturnsCards()
        {
            // Arrange
            await _deleteModel.SetModelContextAsync(_userManager, _deck.OwnerId);

            var deckCards = await _dbContext.Amounts
                .Where(ca => ca.LocationId == _deck.Id)
                .Select(ca => ca.CardId)
                .ToListAsync();

            var deckQuery = _dbContext.Decks
                .Where(d => d.Id == _deck.Id)
                .AsNoTracking();

            var sharedQuery = _dbContext.Amounts
                .Where(ca => ca.Location is Box && deckCards.Contains(ca.CardId))
                .Select(ca => ca.Amount);

            // Act
            var sharedBefore = await sharedQuery.ToListAsync();
            var result = await _deleteModel.OnPostAsync(_deck.Id);

            var sharedAfter = await sharedQuery.ToListAsync();
            var deckAfter = await deckQuery.SingleOrDefaultAsync();

            var sharedChanged = sharedBefore.Zip( sharedAfter,
                (before, after) => (before, after));

            // // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Null(deckAfter);
            Assert.NotEqual(sharedBefore, sharedAfter);
            Assert.All(sharedChanged, ba => Assert.True(ba.before <= ba.after));
        }


        // [Fact]
        // public async Task OnPost_ActiveTrades_CascadesDelete()
        // {
        //     // Arrange
        //     await _deleteModel.SetModelContextAsync(_userManager, _deck.OwnerId);

        //     var cardsQuery = _dbContext.Amounts
        //         .Where(ca => ca.LocationId == _deck.Id)
        //         .AsNoTracking();

        //     var deckQuery = _dbContext.Decks
        //         .Where(d => d.Id == _deck.Id)
        //         .AsNoTracking();

        //     // Act
        //     var cardsBefore = await cardsQuery.ToListAsync();

        //     var target = await _dbContext.Decks
        //         .Include(d => d.Owner)
        //         .FirstAsync(d => d.Id != _deck.Id);

        //     var tradeBefore = new Trade
        //     {
        //         Proposer = _deck.Owner,
        //         Receiver = target.Owner,
        //         To = target,
        //         From = _deck,
        //         Card = cardsBefore[0].Card,
        //         Amount = 2
        //     };

        //     _dbContext.Trades.Attach(tradeBefore);
        //     await _dbContext.SaveChangesAsync();
        //     _dbContext.ChangeTracker.Clear();

        //     var result = await _deleteModel.OnPostAsync(_deck.Id);

        //     var deckAfter = await deckQuery.SingleOrDefaultAsync();
        //     var cardsAfter = await cardsQuery.ToListAsync();
        //     var tradeAfter = await _dbContext.Trades
        //         .AsNoTracking()
        //         .SingleOrDefaultAsync(t => t.Id == tradeBefore.Id);

        //     // // Assert
        //     Assert.IsType<RedirectToPageResult>(result);
        //     Assert.Null(deckAfter);
        //     Assert.Empty(cardsAfter);
        //     Assert.Null(tradeAfter);
        // }
    }
}