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


        public DeleteTests()
        {
            _services = TestFactory.ServiceProvider();
            _dbContext = TestFactory.CardDbContext(_services);
            _userManager = TestFactory.CardUserManager(_services);

            _deleteModel = new DeleteModel(_userManager, _dbContext, Mock.Of<ILogger<DeleteModel>>());
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
            var deck = await _dbContext.CreateDeckAsync();
            var wrongUser = await _dbContext.Users.FirstAsync(u => u.Id != deck.OwnerId);

            await _deleteModel.SetModelContextAsync(_userManager, wrongUser.Id);

            var deckQuery = _dbContext.Decks
                .Where(d => d.Id == deck.Id)
                .AsNoTracking();

            // Act
            var result = await _deleteModel.OnPostAsync(deck.Id);
            var deckAfter = await deckQuery.SingleOrDefaultAsync();

            // // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(deckAfter);
        }


        [Fact]
        public async Task OnPost_InvalidDeck_NoChange()
        {
            // Arrange
            var deck = await _dbContext.CreateDeckAsync();
            var wrongDeck = -1;

            await _deleteModel.SetModelContextAsync(_userManager, deck.OwnerId);

            var deckQuery = _dbContext.Decks
                .Where(d => d.Id == deck.Id)
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
            var deck = await _dbContext.CreateDeckAsync();

            await _deleteModel.SetModelContextAsync(_userManager, deck.OwnerId);

            var deckCards = await _dbContext.Amounts
                .Where(ca => ca.LocationId == deck.Id)
                .Select(ca => ca.CardId)
                .ToListAsync();

            var deckQuery = _dbContext.Decks
                .Where(d => d.Id == deck.Id)
                .AsNoTracking();

            var sharedQuery = _dbContext.Amounts
                .Where(ca => ca.Location is Shared && deckCards.Contains(ca.CardId))
                .Select(ca => ca.Amount);

            // Act
            var sharedBefore = await sharedQuery.ToListAsync();
            var result = await _deleteModel.OnPostAsync(deck.Id);

            var deckAfter = await deckQuery.SingleOrDefaultAsync();
            var sharedAfter = await sharedQuery.ToListAsync();

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
        //     var deck = await dbContext.CreateDeckAsync();

        //     await deleteModel.SetModelContextAsync(userManager, deck.OwnerId);

        //     var cardsQuery = dbContext.Amounts
        //         .Where(ca => ca.LocationId == deck.Id)
        //         .AsNoTracking();

        //     var deckQuery = dbContext.Decks
        //         .Where(d => d.Id == deck.Id)
        //         .AsNoTracking();

        //     // Act
        //     var cardsBefore = await cardsQuery.ToListAsync();

        //     var target = await dbContext.Decks
        //         .Include(d => d.Owner)
        //         .FirstAsync(d => d.Id != deck.Id);

        //     var tradeBefore = new Trade
        //     {
        //         Proposer = deck.Owner,
        //         Receiver = target.Owner,
        //         To = target,
        //         From = deck,
        //         Card = cardsBefore[0].Card,
        //         Amount = 2
        //     };

        //     dbContext.Trades.Attach(tradeBefore);
        //     await dbContext.SaveChangesAsync();
        //     dbContext.ChangeTracker.Clear();

        //     var result = await deleteModel.OnPostAsync(deck.Id);

        //     var deckAfter = await deckQuery.SingleOrDefaultAsync();
        //     var cardsAfter = await cardsQuery.ToListAsync();
        //     var tradeAfter = await dbContext.Trades
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