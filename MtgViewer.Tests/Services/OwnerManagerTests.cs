using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Areas.Identity.Services;
using MtgViewer.Data;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Services;

public class OwnerManagerTests : IAsyncLifetime
{
    private readonly UserManager<CardUser> _userManager;
    private readonly OwnerManager _ownerManager;
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;

    public OwnerManagerTests(
        UserManager<CardUser> userManager,
        OwnerManager referenceManager,
        CardDbContext dbContext,
        TestDataGenerator testGen)
    {
        _userManager = userManager;
        _ownerManager = referenceManager;
        _dbContext = dbContext;
        _testGen = testGen;
    }

    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();

    [Fact]
    public async Task Create_NewUser_Success()
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
        string? userId = await _userManager.GetUserIdAsync(newUser);

        bool ownersBefore = await _dbContext.Owners
            .AnyAsync(o => o.Id == userId);

        bool success = await _ownerManager.CreateAsync(newUser);

        bool ownersAfter = await _dbContext.Owners
            .AnyAsync(o => o.Id == userId);

        Assert.True(result.Succeeded);
        Assert.False(ownersBefore);

        Assert.True(success);
        Assert.True(ownersAfter);
    }

    [Fact]
    public async Task Create_ExistingUser_Fails()
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
        string? userId = await _userManager.GetUserIdAsync(newUser);

        bool firstCreate = await _ownerManager.CreateAsync(newUser);

        bool ownersBefore = await _dbContext.Owners
            .AnyAsync(o => o.Id == userId);

        bool secondCreate = await _ownerManager.CreateAsync(newUser);

        bool ownersAfter = await _dbContext.Owners
            .AnyAsync(o => o.Id == userId);

        Assert.True(firstCreate);
        Assert.True(result.Succeeded);

        Assert.True(ownersBefore);
        Assert.False(secondCreate);
        Assert.True(ownersAfter);
    }

    [Fact]
    public async Task Create_NonExistingUser_Fails()
    {
        var newUser = new CardUser
        {
            DisplayName = "New User",
            UserName = "user@gmail.com",
            Email = "user@gmail.com",
            IsApproved = true,
            EmailConfirmed = true
        };

        string? userId = await _userManager.GetUserIdAsync(newUser);

        bool ownersBefore = await _dbContext.Owners
            .AnyAsync(o => o.Id == userId);

        bool success = await _ownerManager.CreateAsync(newUser);

        bool ownerAfters = await _dbContext.Owners
            .AnyAsync(o => o.Id == userId);

        Assert.False(ownersBefore);
        Assert.False(success);
        Assert.False(ownerAfters);
    }

    [Fact]
    public async Task Update_NameChange_Success()
    {
        var user = await _userManager.Users.FirstAsync();

        string oldName = await _dbContext.Owners
            .Where(o => o.Id == user.Id)
            .Select(o => o.Name)
            .FirstAsync();

        user.DisplayName = "This is a new name";

        var result = await _userManager.UpdateAsync(user);

        bool success = await _ownerManager.UpdateAsync(user);

        string newName = await _dbContext.Owners
            .Where(o => o.Id == user.Id)
            .Select(o => o.Name)
            .FirstAsync();

        Assert.NotEqual(oldName, user.DisplayName);

        Assert.True(result.Succeeded);
        Assert.True(success);
        Assert.Equal(user.DisplayName, newName);
    }

    [Fact]
    public async Task Update_NoChange_Success()
    {
        var user = await _userManager.Users.FirstAsync();

        string oldName = await _dbContext.Owners
            .Where(o => o.Id == user.Id)
            .Select(o => o.Name)
            .FirstAsync();

        bool success = await _ownerManager.UpdateAsync(user);

        string newName = await _dbContext.Owners
            .Where(o => o.Id == user.Id)
            .Select(o => o.Name)
            .FirstAsync();

        Assert.Equal(oldName, newName);
        Assert.True(success);
        Assert.Equal(user.DisplayName, newName);
    }

    [Fact]
    public async Task Delete_CardUserExists_Fails()
    {
        var user = await _userManager.Users.FirstAsync();

        bool beforeDelete = await _dbContext.Owners.AnyAsync(o => o.Id == user.Id);
        bool success = await _ownerManager.DeleteAsync(user);
        bool afterDelete = await _dbContext.Owners.AnyAsync(o => o.Id == user.Id);

        Assert.True(beforeDelete);
        Assert.False(success);
        Assert.True(afterDelete);
    }

    [Fact]
    public async Task Delete_CardUserDeleted_Success()
    {
        var user = await _userManager.Users.FirstAsync();
        var result = await _userManager.DeleteAsync(user);

        bool beforeDelete = await _dbContext.Owners.AnyAsync(o => o.Id == user.Id);
        bool success = await _ownerManager.DeleteAsync(user);
        bool afterDelete = await _dbContext.Owners.AnyAsync(o => o.Id == user.Id);

        Assert.True(result.Succeeded);

        Assert.True(beforeDelete);
        Assert.True(success);
        Assert.False(afterDelete);
    }

    [Fact]
    public async Task Delete_CardUserDeleted_OwnedCardsReturned()
    {
        var user = await _userManager.Users.FirstAsync();

        var owner = await _ownerManager.Owners
            .FirstAsync(o => o.Id == user.Id);

        await _testGen.AddUserCardsAsync(owner);

        int userCards = await _dbContext.Holds
            .Where(h => h.Location is Deck
                && (h.Location as Deck)!.OwnerId == user.Id)
            .SumAsync(h => h.Copies);

        int treasuryBefore = await _dbContext.Holds
            .Where(h => h.Location is Box)
            .SumAsync(h => h.Copies);

        var result = await _userManager.DeleteAsync(user);

        bool beforeDelete = await _dbContext.Owners.AnyAsync(o => o.Id == user.Id);
        bool success = await _ownerManager.DeleteAsync(user);
        bool afterDelete = await _dbContext.Owners.AnyAsync(o => o.Id == user.Id);

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
        var allOwners = _dbContext.Owners.AsAsyncEnumerable();

        await foreach (var owner in allOwners)
        {
            owner.ResetRequested = true;
        }

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();

        bool cardsExistBefore = await _dbContext.Cards.AnyAsync();
        bool decksExistBefore = await _dbContext.Decks.AnyAsync();
        bool boxesExistBefore = await _dbContext.Boxes.AnyAsync();

        await _ownerManager.ResetAsync();

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
