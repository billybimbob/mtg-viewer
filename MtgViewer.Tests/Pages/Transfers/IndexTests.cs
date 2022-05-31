using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MtgViewer.Data;
using MtgViewer.Pages.Transfers;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Pages.Transfers;

public class IndexTests : IAsyncLifetime
{
    private readonly IndexModel _indexModel;
    private readonly CardDbContext _dbContext;
    private readonly ActionHandlerFactory _pageFactory;
    private readonly TestDataGenerator _testGen;

    public IndexTests(
        IndexModel indexModel,
        CardDbContext dbContext,
        ActionHandlerFactory pageFactory,
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

        await _pageFactory.AddPageContextAsync(_indexModel, suggestion.ReceiverId);

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

        string wrongUser = await _dbContext.Owners
            .Select(o => o.Id)
            .FirstAsync(oid => oid != suggestion.ReceiverId);

        await _pageFactory.AddPageContextAsync(_indexModel, wrongUser);

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

        const int invalidSuggestId = 0;

        await _pageFactory.AddPageContextAsync(_indexModel, suggestion.ReceiverId);

        // Act
        var suggestsBefore = await AllSuggestions.Select(t => t.Id).ToListAsync();
        var result = await _indexModel.OnPostAsync(invalidSuggestId, default);
        var suggestsAFter = await AllSuggestions.Select(t => t.Id).ToListAsync();

        // Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(suggestsBefore, suggestsAFter);
    }
}
