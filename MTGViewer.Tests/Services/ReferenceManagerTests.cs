using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;
using MTGViewer.Data;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Services;

public class ReferenceManagerTests : IAsyncLifetime
{
    private readonly UserManager<CardUser> _userManager;
    private readonly ReferenceManager _referenceManager;
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;

    public ReferenceManagerTests(
        UserManager<CardUser> userManager,
        ReferenceManager referenceManager,
        CardDbContext dbContext,
        TestDataGenerator testGen)
    {
        _userManager = userManager;
        _referenceManager = referenceManager;
        _dbContext = dbContext;
        _testGen = testGen;
    }


    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();



    [Fact]
    public async Task CreateReference_NewUser_Success()
    {
        var newUser = new CardUser
        {
            DisplayName = "New User",
            UserName = "user@gmail.com",
            Email = "user@gmail.com",
            IsApproved = true,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(newUser);
        var userId = await _userManager.GetUserIdAsync(newUser);

        bool userBefore = await _dbContext.Users
            .AnyAsync(u => u.Id == userId);

        bool success = await _referenceManager.CreateReferenceAsync(newUser);

        bool userAfter = await _dbContext.Users
            .AnyAsync(u => u.Id == userId);

        Assert.True(result.Succeeded);
        Assert.False(userBefore);

        Assert.True(success);
        Assert.True(userAfter);
    }


    [Fact]
    public async Task CreateReference_ExistingUser_Fails()
    {
        var newUser = new CardUser
        {
            DisplayName = "New User",
            UserName = "user@gmail.com",
            Email = "user@gmail.com",
            IsApproved = true,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(newUser);
        var userId = await _userManager.GetUserIdAsync(newUser);

        bool firstCreate = await _referenceManager.CreateReferenceAsync(newUser);

        bool userBefore = await _dbContext.Users
            .AnyAsync(u => u.Id == userId);

        bool secondCreate = await _referenceManager.CreateReferenceAsync(newUser);

        bool userAfter = await _dbContext.Users
            .AnyAsync(u => u.Id == userId);

        Assert.True(firstCreate);
        Assert.True(result.Succeeded);

        Assert.True(userBefore);
        Assert.False(secondCreate);
        Assert.True(userAfter);
    }


    [Fact]
    public async Task CreateReference_NonExistingUser_Fails()
    {
        var newUser = new CardUser
        {
            DisplayName = "New User",
            UserName = "user@gmail.com",
            Email = "user@gmail.com",
            IsApproved = true,
            EmailConfirmed = true
        };

        var userId = await _userManager.GetUserIdAsync(newUser);

        bool userBefore = await _dbContext.Users
            .AnyAsync(u => u.Id == userId);

        bool success = await _referenceManager.CreateReferenceAsync(newUser);

        bool userAfter = await _dbContext.Users
            .AnyAsync(u => u.Id == userId);

        Assert.False(userBefore);
        Assert.False(success);
        Assert.False(userAfter);
    }


    [Fact]
    public async Task UpdateReference_NameChange_Success()
    {
        var user = await _userManager.Users.FirstAsync();

        string oldRefName = await _dbContext.Users
            .Where(u => u.Id == user.Id)
            .Select(u => u.Name)
            .FirstAsync();

        string oldName = user.DisplayName;

        user.DisplayName = "This is a new name";

        var result = await _userManager.UpdateAsync(user);

        bool success = await _referenceManager.UpdateReferenceAsync(user);

        string newRefName = await _dbContext.Users
            .Where(u => u.Id == user.Id)
            .Select(u => u.Name)
            .FirstAsync();

        Assert.Equal(oldName, oldRefName);
        Assert.NotEqual(oldName, user.DisplayName);

        Assert.True(result.Succeeded);
        Assert.True(success);
        Assert.Equal(user.DisplayName, newRefName);
    }


    [Fact]
    public async Task UpdateReference_NoChange_Success()
    {
        var user = await _userManager.Users.FirstAsync();

        string oldRefName = await _dbContext.Users
            .Where(u => u.Id == user.Id)
            .Select(u => u.Name)
            .FirstAsync();

        bool success = await _referenceManager.UpdateReferenceAsync(user);

        string newRefName = await _dbContext.Users
            .Where(u => u.Id == user.Id)
            .Select(u => u.Name)
            .FirstAsync();

        Assert.Equal(oldRefName, newRefName);
        Assert.True(success);
        Assert.Equal(user.DisplayName, newRefName);
    }


    [Fact]
    public async Task DeleteReference_CardUserExists_Fails()
    {
        var user = await _userManager.Users.FirstAsync();

        bool beforeDelete = await _dbContext.Users.AnyAsync(u => u.Id == user.Id);
        bool success = await _referenceManager.DeleteReferenceAsync(user);
        bool afterDelete = await _dbContext.Users.AnyAsync(u => u.Id == user.Id);

        Assert.True(beforeDelete);
        Assert.False(success);
        Assert.True(afterDelete);
    }


    [Fact]
    public async Task DeleteReference_CardUserDeleted_Success()
    {
        var user = await _userManager.Users.FirstAsync();
        var result = await _userManager.DeleteAsync(user);

        bool beforeDelete = await _dbContext.Users.AnyAsync(u => u.Id == user.Id);
        bool success = await _referenceManager.DeleteReferenceAsync(user);
        bool afterDelete = await _dbContext.Users.AnyAsync(u => u.Id == user.Id);

        Assert.True(result.Succeeded);

        Assert.True(beforeDelete);
        Assert.True(success);
        Assert.False(afterDelete);
    }


    [Fact]
    public async Task DeleteReference_CardUserDeleted_CardsReturned()
    {
        var user = await _userManager.Users.FirstAsync();

        var userRef = await _referenceManager.References
            .FirstAsync(u => u.Id == user.Id);

        await _testGen.AddUserCardsAsync(userRef);

        int userCards = await _dbContext.Holds
            .Where(h => h.Location is Deck
                && (h.Location as Deck)!.OwnerId == user.Id)
            .SumAsync(h => h.Copies);

        int treasuryBefore = await _dbContext.Holds
            .Where(h => h.Location is Box)
            .SumAsync(h => h.Copies);

        var result = await _userManager.DeleteAsync(user);

        bool beforeDelete = await _dbContext.Users.AnyAsync(u => u.Id == user.Id);
        bool success = await _referenceManager.DeleteReferenceAsync(user);
        bool afterDelete = await _dbContext.Users.AnyAsync(u => u.Id == user.Id);

        int treasuryAfter = await _dbContext.Holds
            .Where(h => h.Location is Box)
            .SumAsync(h => h.Copies);

        Assert.True(result.Succeeded);

        Assert.True(beforeDelete);
        Assert.True(success);
        Assert.False(afterDelete);

        Assert.Equal(userCards, treasuryAfter - treasuryBefore);
    }


    [Fact]
    public async Task Reset_AllResetting_CardsEmpty()
    {
        var allUsers = _dbContext.Users.AsAsyncEnumerable();

        await foreach(var user in allUsers)
        {
            user.ResetRequested = true;
        }

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();

        bool cardsExistBefore = await _dbContext.Cards.AnyAsync();
        bool decksExistBefore = await _dbContext.Decks.AnyAsync();
        bool boxesExistBefore = await _dbContext.Boxes.AnyAsync();

        await _referenceManager.ApplyResetAsync();

        bool cardsExistAfter = await _dbContext.Cards.AnyAsync();
        bool decksExistAfter = await _dbContext.Decks.AnyAsync();
        bool boxesExistAfter = await _dbContext.Boxes.AnyAsync();

        Assert.True(cardsExistBefore);
        Assert.True(decksExistBefore);
        Assert.True(boxesExistBefore);

        Assert.False(cardsExistAfter);
        Assert.False(decksExistAfter);
        Assert.False(boxesExistAfter);
    }
}