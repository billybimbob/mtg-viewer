using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Decks;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages.Decks
{
    public class DeleteTests
    {
        [Fact]
        public async Task OnPost_WrongUser_NoChange()
        {
            // Arrange
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var deleteModel = new DeleteModel(userManager, dbContext, Mock.Of<ILogger<DeleteModel>>());
            var deck = await dbContext.CreateDeckAsync();
            var wrongUser = await dbContext.Users.FirstAsync(u => u.Id != deck.OwnerId);

            await deleteModel.SetModelContextAsync(userManager, wrongUser.Id);

            var deckQuery = dbContext.Decks
                .Where(d => d.Id == deck.Id)
                .AsNoTracking();

            // Act
            var result = await deleteModel.OnPostAsync(deck.Id);
            var deckAfter = await deckQuery.SingleOrDefaultAsync();

            // // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(deckAfter);
        }


        [Fact]
        public async Task OnPost_InvalidDeck_NoChange()
        {
            // Arrange
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var deleteModel = new DeleteModel(userManager, dbContext, Mock.Of<ILogger<DeleteModel>>());
            var deck = await dbContext.CreateDeckAsync();
            var wrongDeck = -1;

            await deleteModel.SetModelContextAsync(userManager, deck.OwnerId);

            var deckQuery = dbContext.Decks
                .Where(d => d.Id == deck.Id)
                .AsNoTracking();

            // Act
            var result = await deleteModel.OnPostAsync(wrongDeck);
            var deckAfter = await deckQuery.SingleOrDefaultAsync();

            // // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(deckAfter);
        }


        [Fact]
        public async Task OnPost_ValidDeck_ReturnsCards()
        {
            // Arrange
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var deleteModel = new DeleteModel(userManager, dbContext, Mock.Of<ILogger<DeleteModel>>());
            var deck = await dbContext.CreateDeckAsync();

            await deleteModel.SetModelContextAsync(userManager, deck.OwnerId);

            var deckCards = await dbContext.Amounts
                .Where(ca => ca.LocationId == deck.Id)
                .Select(ca => ca.CardId)
                .ToListAsync();

            var deckQuery = dbContext.Decks
                .Where(d => d.Id == deck.Id)
                .AsNoTracking();

            var sharedQuery = dbContext.Amounts
                .Where(ca => ca.Location is Shared && deckCards.Contains(ca.CardId))
                .Select(ca => ca.Amount);

            // Act
            var sharedBefore = await sharedQuery.ToListAsync();
            var result = await deleteModel.OnPostAsync(deck.Id);

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
        //     await using var services = TestFactory.ServiceProvider();
        //     await using var dbContext = TestFactory.CardDbContext(services);
        //     using var userManager = TestFactory.CardUserManager(services);

        //     await dbContext.SeedAsync(userManager);

        //     var deleteModel = new DeleteModel(userManager, dbContext, Mock.Of<ILogger<DeleteModel>>());
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