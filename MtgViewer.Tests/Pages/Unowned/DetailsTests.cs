using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MtgViewer.Data;
using MtgViewer.Pages.Unowned;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Pages.Unowned;

public class IndexTests : IAsyncLifetime
{
    private readonly DetailsModel _detailsModel;
    private readonly CardDbContext _dbContext;
    private readonly ActionHandlerFactory _pageFactory;
    private readonly TestDataGenerator _testGen;
    private Unclaimed _unclaimed = default!;

    public IndexTests(
        DetailsModel indexModel,
        CardDbContext dbContext,
        ActionHandlerFactory pageFactory,
        TestDataGenerator testGen)
    {
        _detailsModel = indexModel;
        _dbContext = dbContext;
        _pageFactory = pageFactory;
        _testGen = testGen;
    }

    public async Task InitializeAsync()
    {
        await _testGen.SeedAsync();

        _unclaimed = await _testGen.CreateUnclaimedAsync();
    }

    public Task DisposeAsync() => _testGen.ClearAsync();

    [Fact]
    public async Task OnPostClaim_NoUser_NoChange()
    {
        _pageFactory.AddPageContext(_detailsModel);

        bool unclaimedBefore = await _dbContext.Unclaimed
            .Select(u => u.Id)
            .ContainsAsync(_unclaimed.Id);

        var result = await _detailsModel.OnPostClaimAsync(_unclaimed.Id, default);

        bool unclaimedAfter = await _dbContext.Unclaimed
            .Select(u => u.Id)
            .ContainsAsync(_unclaimed.Id);

        Assert.IsType<NotFoundResult>(result);

        Assert.True(unclaimedBefore);
        Assert.True(unclaimedAfter);
    }

    [Fact]
    public async Task OnPostClaim_WithUser_AppliesClaim()
    {
        string userId = await _dbContext.Owners
            .Select(o => o.Id)
            .FirstAsync();

        await _pageFactory.AddPageContextAsync(_detailsModel, userId);

        bool unclaimedBefore = await _dbContext.Unclaimed
            .Select(u => u.Id)
            .ContainsAsync(_unclaimed.Id);

        int userDecksBefore = await _dbContext.Decks
            .Where(d => d.OwnerId == userId)
            .CountAsync();

        var result = await _detailsModel.OnPostClaimAsync(_unclaimed.Id, default);

        bool unclaimedAfter = await _dbContext.Unclaimed
            .Select(u => u.Id)
            .ContainsAsync(_unclaimed.Id);

        int userDecksAfter = await _dbContext.Decks
            .Where(d => d.OwnerId == userId)
            .CountAsync();

        Assert.IsType<RedirectToPageResult>(result);

        Assert.True(unclaimedBefore);
        Assert.False(unclaimedAfter);

        Assert.Equal(1, userDecksAfter - userDecksBefore);
    }

    [Fact]
    public async Task OnPostRemove_WithUser_NoChange()
    {
        string userId = await _dbContext.Owners
            .Select(o => o.Id)
            .FirstAsync();

        await _pageFactory.AddPageContextAsync(_detailsModel, userId);

        bool unclaimedBefore = await _dbContext.Unclaimed
            .Select(u => u.Id)
            .ContainsAsync(_unclaimed.Id);

        var result = await _detailsModel.OnPostRemoveAsync(_unclaimed.Id, default);

        bool unclaimedAfter = await _dbContext.Unclaimed
            .Select(u => u.Id)
            .ContainsAsync(_unclaimed.Id);

        Assert.IsType<RedirectToPageResult>(result);

        Assert.True(unclaimedBefore);
        Assert.False(unclaimedAfter);
    }
}
