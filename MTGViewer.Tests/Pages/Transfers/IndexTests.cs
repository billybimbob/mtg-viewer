using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Transfers;

public class IndexTests : IAsyncLifetime
{
    private readonly IndexModel _indexModel;
    private readonly CardDbContext _dbContext;
    private readonly PageContextFactory _pageFactory;
    private readonly TestDataGenerator _testGen;

    public IndexTests(
        IndexModel indexModel,
        CardDbContext dbContext,
        PageContextFactory pageFactory,
        TestDataGenerator testGen)
    {
        _indexModel = indexModel;
        _dbContext = dbContext;
        _pageFactory = pageFactory;
        _testGen = testGen;
    }

    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();

    private IQueryable<Suggestion> AllSuggestions =>
        _dbContext.Suggestions
            .AsNoTracking()
            .OrderBy(s => s.Id);

    [Fact]
    public async Task OnPost_ValidSuggestion_RemovesSuggestion()
    {
        // Arrange
        var suggestion = await AllSuggestions.FirstAsync();

        await _pageFactory.AddModelContextAsync(_indexModel, suggestion.ReceiverId);

        // Act
        var result = await _indexModel.OnPostAsync(suggestion.Id, default);
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

        await _pageFactory.AddModelContextAsync(_indexModel, wrongUser);

        // Act
        var result = await _indexModel.OnPostAsync(suggestion.Id, default);
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

        await _pageFactory.AddModelContextAsync(_indexModel, suggestion.ReceiverId);

        // Act
        var suggestsBefore = await AllSuggestions.Select(t => t.Id).ToListAsync();
        var result = await _indexModel.OnPostAsync(invalidSuggestId, default);
        var suggestsAFter = await AllSuggestions.Select(t => t.Id).ToListAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(suggestsBefore, suggestsAFter);
    }
}
