using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages.Transfers
{
    public class IndexTests
    {
        [Fact]
        public async Task OnPost_ValidSuggestion_RemovesSuggestion()
        {
            // Arrange
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var indexModel = new IndexModel(userManager, dbContext);
            var suggestQuery = dbContext.Suggestions.AsNoTracking();
            var suggestion = await suggestQuery.FirstAsync();

            await indexModel.SetModelContextAsync(userManager, suggestion.ReceiverId);

            // Act
            var result = await indexModel.OnPostAsync(suggestion.Id);
            var suggestions = await suggestQuery.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.DoesNotContain(suggestion.Id, suggestions);
        }


        [Fact]
        public async Task OnPost_WrongUser_NoRemove()
        {
            // Arrange
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var indexModel = new IndexModel(userManager, dbContext);
            var suggestQuery = dbContext.Suggestions.AsNoTracking();
            var suggestion = await suggestQuery.FirstAsync();

            await indexModel.SetModelContextAsync(userManager, suggestion.ProposerId);

            // Act
            var result = await indexModel.OnPostAsync(suggestion.Id);
            var suggestions = await suggestQuery.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Contains(suggestion.Id, suggestions);
        }


        [Fact]
        public async Task OnPost_InvalidSuggestion_NoRemove()
        {
            // Arrange
            await using var services = TestFactory.ServiceProvider();
            await using var dbContext = TestFactory.CardDbContext(services);
            using var userManager = TestFactory.CardUserManager(services);

            await dbContext.SeedAsync(userManager);

            var indexModel = new IndexModel(userManager, dbContext);
            var tradeQuery = dbContext.Trades.AsNoTracking();
            var nonSuggestion = await tradeQuery.FirstAsync();

            await indexModel.SetModelContextAsync(userManager, nonSuggestion.ReceiverId);

            // Act
            var result = await indexModel.OnPostAsync(nonSuggestion.Id);
            var suggestions = await tradeQuery.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Contains(nonSuggestion.Id, suggestions);
        }
   }
}