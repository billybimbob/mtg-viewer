using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Unowned;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Unowned;

public class IndexTests : IAsyncLifetime
{
    private readonly DetailsModel _detailsModel;
    private readonly CardDbContext _dbContext;
    private readonly PageContextFactory _pageFactory;
    private readonly TestDataGenerator _testGen;
    private Unclaimed _unclaimed = default!;

    public IndexTests(
        DetailsModel indexModel,
        CardDbContext dbContext,
        PageContextFactory pageFactory,
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
        _pageFactory.AddModelContext(_detailsModel);

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
        var userId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync();

        await _pageFactory.AddModelContextAsync(_detailsModel, userId);

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
        var userId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync();

        await _pageFactory.AddModelContextAsync(_detailsModel, userId);

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