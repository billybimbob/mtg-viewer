using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages.Transfers
{
    public class IndexTests : IAsyncLifetime
    {
        private readonly ServiceProvider _services;
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        private readonly IndexModel _indexModel;

        public IndexTests()
        {
            _services = TestFactory.ServiceProvider();
            _dbContext = TestFactory.CardDbContext(_services);
            _userManager = TestFactory.CardUserManager(_services);

            _indexModel = new(_userManager, _dbContext);
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
        public async Task OnPost_ValidSuggestion_RemovesSuggestion()
        {
            // Arrange
            var suggestQuery = _dbContext.Suggestions.AsNoTracking();
            var suggestion = await suggestQuery.FirstAsync();

            await _indexModel.SetModelContextAsync(_userManager, suggestion.ReceiverId);

            // Act
            var result = await _indexModel.OnPostAsync(suggestion.Id);
            var suggestions = await suggestQuery.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.DoesNotContain(suggestion.Id, suggestions);
        }


        [Fact]
        public async Task OnPost_WrongUser_NoRemove()
        {
            // Arrange
            var suggestQuery = _dbContext.Suggestions.AsNoTracking();
            var suggestion = await suggestQuery.FirstAsync();

            await _indexModel.SetModelContextAsync(_userManager, suggestion.ProposerId);

            // Act
            var result = await _indexModel.OnPostAsync(suggestion.Id);
            var suggestions = await suggestQuery.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Contains(suggestion.Id, suggestions);
        }


        [Fact]
        public async Task OnPost_InvalidSuggestion_NoRemove()
        {
            // Arrange
            var tradeQuery = _dbContext.Exchanges.AsNoTracking();
            var nonSuggestion = await tradeQuery.FirstAsync();

            await _indexModel.SetModelContextAsync(_userManager, nonSuggestion.ReceiverId);

            // Act
            var result = await _indexModel.OnPostAsync(nonSuggestion.Id);
            var trades = await tradeQuery.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Contains(nonSuggestion.Id, trades);
        }
   }
}