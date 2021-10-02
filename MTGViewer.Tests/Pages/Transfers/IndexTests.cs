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


        public Task InitializeAsync()
        {
            return _dbContext.SeedAsync(_userManager);
        }


        public async Task DisposeAsync()
        {
            await _services.DisposeAsync();
            await _dbContext.DisposeAsync();
            _userManager.Dispose();
        }


        private IQueryable<Suggestion> AllSuggestions =>
            _dbContext.Suggestions
                .AsNoTracking()
                .OrderBy(s => s.Id);


        [Fact]
        public async Task OnPost_ValidSuggestion_RemovesSuggestion()
        {
            // Arrange
            var suggestion = await AllSuggestions.FirstAsync();

            await _indexModel.SetModelContextAsync(_userManager, suggestion.ReceiverId);

            // Act
            var result = await _indexModel.OnPostAsync(suggestion.Id);
            var suggestions = await AllSuggestions.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.DoesNotContain(suggestion.Id, suggestions);
        }


        [Fact]
        public async Task OnPost_WrongUser_NoRemove()
        {
            // Arrange
            var suggestion = await AllSuggestions.FirstAsync();

            var wrongUser = await _dbContext.Users
                .Select(u => u.Id)
                .FirstAsync(uid => uid != suggestion.ReceiverId);

            await _indexModel.SetModelContextAsync(_userManager, wrongUser);

            // Act
            var result = await _indexModel.OnPostAsync(suggestion.Id);
            var suggestions = await AllSuggestions.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Contains(suggestion.Id, suggestions);
        }


        [Fact]
        public async Task OnPost_InvalidSuggestion_NoRemove()
        {
            // Arrange
            var suggestion = await AllSuggestions.FirstAsync();
            var invalidSuggestId = 0;

            await _indexModel.SetModelContextAsync(_userManager, suggestion.ReceiverId);

            // Act
            var suggestsBefore = await AllSuggestions.Select(t => t.Id).ToListAsync();
            var result = await _indexModel.OnPostAsync(invalidSuggestId);
            var suggestsAFter = await AllSuggestions.Select(t => t.Id).ToListAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(suggestsBefore, suggestsAFter);
        }
   }
}