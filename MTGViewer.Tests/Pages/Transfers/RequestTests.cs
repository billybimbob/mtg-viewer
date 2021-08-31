using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages.Transfers
{
    public class RequestTests
    {
        [Fact]
        public async Task OnPost_WrongUser_NoChange()
        {
            // Arrange
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var deck = await dbContext.CreateRequestDeckAsync();
            var requestModel = new RequestModel(dbContext, userManager, Mock.Of<ILogger<RequestModel>>());

            var wrongUser = await dbContext.Users.FirstAsync(u => u.Id != deck.Owner.Id);
            var tradesQuery = dbContext.Trades.AsNoTracking();

            await requestModel.SetModelContextAsync(userManager, wrongUser.Id);

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await requestModel.OnPostAsync(deck.Id);
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
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var deck = await dbContext.CreateRequestDeckAsync();
            var requestModel = new RequestModel(dbContext, userManager, Mock.Of<ILogger<RequestModel>>());

            var tradesQuery = dbContext.Trades.AsNoTracking();
            var wrongDeck = dbContext.Decks
                .AsNoTracking()
                .FirstAsync(t => t.Id != deck.Id);

            await requestModel.SetModelContextAsync(userManager, deck.Owner.Id);

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await requestModel.OnPostAsync(wrongDeck.Id);
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
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var deck = await dbContext.CreateRequestDeckAsync();
            var requestModel = new RequestModel(dbContext, userManager, Mock.Of<ILogger<RequestModel>>());
            var tradesQuery = dbContext.Trades.AsNoTracking();

            await requestModel.SetModelContextAsync(userManager, deck.Owner.Id);

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await requestModel.OnPostAsync(deck.Id);

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
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var deck = await dbContext.CreateRequestDeckAsync();
            var requestModel = new RequestModel(dbContext, userManager, Mock.Of<ILogger<RequestModel>>());
            var tradesQuery = dbContext.Trades.AsNoTracking();

            await requestModel.SetModelContextAsync(userManager, deck.Owner.Id);

            var requestCard = await dbContext.Amounts
                .Where(ca => ca.LocationId == deck.Id && ca.IsRequest)
                .Select(ca => ca.Card)
                .AsNoTracking()
                .FirstAsync();

            var nonOwner = await dbContext.Users
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

            dbContext.Decks.AttachRange(extraLocations);
            dbContext.Amounts.AttachRange(amounts);

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            // Act
            var tradesBefore = await tradesQuery.ToListAsync();
            var result = await requestModel.OnPostAsync(deck.Id);

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