using System.Linq;
using System.Threading.Tasks;
using Xunit;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Pages.Unowned;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Unowned;

public class IndexTests : IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private TestDataGenerator _testGen;

    private readonly IndexModel _indexModel;
    private Unclaimed _unclaimed = null!;

    public IndexTests(
        CardDbContext dbContext,
        PageSizes pageSizes,
        SignInManager<CardUser> signInManager,
        UserManager<CardUser> userManager,
        ILogger<IndexModel> logger,
        TestDataGenerator testGen)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _testGen = testGen;

        _indexModel = new(
            dbContext, pageSizes, signInManager, userManager, logger);
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
        _indexModel.SetModelContext();

        bool unclaimedBefore = await _dbContext.Unclaimed
            .Select(u => u.Id)
            .ContainsAsync(_unclaimed.Id);

        var result = await _indexModel.OnPostClaimAsync(_unclaimed.Id, default);

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

        await _indexModel.SetModelContextAsync(_userManager, userId);

        bool unclaimedBefore = await _dbContext.Unclaimed
            .Select(u => u.Id)
            .ContainsAsync(_unclaimed.Id);

        int userDecksBefore = await _dbContext.Decks
            .Where(d => d.OwnerId == userId)
            .CountAsync();

        var result = await _indexModel.OnPostClaimAsync(_unclaimed.Id, default);

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
    public async Task OnPostRemove_NoUser_NoChange()
    {
        _indexModel.SetModelContext();

        bool unclaimedBefore = await _dbContext.Unclaimed
            .Select(u => u.Id)
            .ContainsAsync(_unclaimed.Id);

        var result = await _indexModel.OnPostRemoveAsync(_unclaimed.Id, default);

        bool unclaimedAfter = await _dbContext.Unclaimed
            .Select(u => u.Id)
            .ContainsAsync(_unclaimed.Id);

        Assert.IsType<NotFoundResult>(result);

        Assert.True(unclaimedBefore);
        Assert.True(unclaimedAfter);
    }


    [Fact]
    public async Task OnPostRemove_WithUser_NoChange()
    {
        var userId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync();

        await _indexModel.SetModelContextAsync(_userManager, userId);

        bool unclaimedBefore = await _dbContext.Unclaimed
            .Select(u => u.Id)
            .ContainsAsync(_unclaimed.Id);

        var result = await _indexModel.OnPostRemoveAsync(_unclaimed.Id, default);

        bool unclaimedAfter = await _dbContext.Unclaimed
            .Select(u => u.Id)
            .ContainsAsync(_unclaimed.Id);

        Assert.IsType<RedirectToPageResult>(result);

        Assert.True(unclaimedBefore);
        Assert.False(unclaimedAfter);
    }
}