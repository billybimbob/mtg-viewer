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

public class PlayerManagerTests : IAsyncLifetime
{
    private readonly UserManager<CardUser> _userManager;
    private readonly PlayerManager _playerManager;
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;

    public PlayerManagerTests(
        UserManager<CardUser> userManager,
        PlayerManager playerManager,
        CardDbContext dbContext,
        TestDataGenerator testGen)
    {
        _userManager = userManager;
        _playerManager = playerManager;
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

        bool playersBefore = await _dbContext.Players
            .AnyAsync(p => p.Id == userId);

        bool success = await _playerManager.CreateAsync(newUser);

        bool playersAfter = await _dbContext.Players
            .AnyAsync(p => p.Id == userId);

        Assert.True(result.Succeeded);
        Assert.False(playersBefore);

        Assert.True(success);
        Assert.True(playersAfter);
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

        bool firstCreate = await _playerManager.CreateAsync(newUser);

        bool playersBefore = await _dbContext.Players
            .AnyAsync(p => p.Id == userId);

        bool secondCreate = await _playerManager.CreateAsync(newUser);

        bool playersAfter = await _dbContext.Players
            .AnyAsync(p => p.Id == userId);

        Assert.True(firstCreate);
        Assert.True(result.Succeeded);

        Assert.True(playersBefore);
        Assert.False(secondCreate);
        Assert.True(playersAfter);
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

        bool playersBefore = await _dbContext.Players
            .AnyAsync(p => p.Id == userId);

        bool success = await _playerManager.CreateAsync(newUser);

        bool playersAfter = await _dbContext.Players
            .AnyAsync(p => p.Id == userId);

        Assert.False(playersBefore);
        Assert.False(success);
        Assert.False(playersAfter);
    }

    [Fact]
    public async Task Update_NameChange_Success()
    {
        var user = await _userManager.Users.FirstAsync();

        string oldName = await _dbContext.Players
            .Where(p => p.Id == user.Id)
            .Select(p => p.Name)
            .FirstAsync();

        user.DisplayName = "This is a new name";

        var result = await _userManager.UpdateAsync(user);

        bool success = await _playerManager.UpdateAsync(user);

        string newName = await _dbContext.Players
            .Where(p => p.Id == user.Id)
            .Select(p => p.Name)
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

        string oldName = await _dbContext.Players
            .Where(p => p.Id == user.Id)
            .Select(p => p.Name)
            .FirstAsync();

        bool success = await _playerManager.UpdateAsync(user);

        string newName = await _dbContext.Players
            .Where(p => p.Id == user.Id)
            .Select(p => p.Name)
            .FirstAsync();

        Assert.Equal(oldName, newName);
        Assert.True(success);
        Assert.Equal(user.DisplayName, newName);
    }

    [Fact]
    public async Task Delete_CardUserExists_Fails()
    {
        var user = await _userManager.Users.FirstAsync();

        bool beforeDelete = await _dbContext.Players
            .AnyAsync(p => p.Id == user.Id);

        bool success = await _playerManager.DeleteAsync(user);

        bool afterDelete = await _dbContext.Players
            .AnyAsync(p => p.Id == user.Id);

        Assert.True(beforeDelete);
        Assert.False(success);
        Assert.True(afterDelete);
    }

    [Fact]
    public async Task Delete_CardUserDeleted_Success()
    {
        var user = await _userManager.Users.FirstAsync();
        var result = await _userManager.DeleteAsync(user);

        bool beforeDelete = await _dbContext.Players
            .AnyAsync(p => p.Id == user.Id);

        bool success = await _playerManager.DeleteAsync(user);

        bool afterDelete = await _dbContext.Players
            .AnyAsync(p => p.Id == user.Id);

        Assert.True(result.Succeeded);

        Assert.True(beforeDelete);
        Assert.True(success);
        Assert.False(afterDelete);
    }

    [Fact]
    public async Task Delete_CardUserDeleted_OwnedCardsReturned()
    {
        var user = await _userManager.Users.FirstAsync();

        var player = await _playerManager.Players
            .FirstAsync(p => p.Id == user.Id);

        await _testGen.AddPlayerCardsAsync(player);

        int userCards = await _dbContext.Holds
            .Where(h => h.Location is Deck
                && (h.Location as Deck)!.OwnerId == user.Id)
            .SumAsync(h => h.Copies);

        int treasuryBefore = await _dbContext.Holds
            .Where(h => h.Location is Box)
            .SumAsync(h => h.Copies);

        var result = await _userManager.DeleteAsync(user);

        bool beforeDelete = await _dbContext.Players
            .AnyAsync(p => p.Id == user.Id);

        bool success = await _playerManager.DeleteAsync(user);

        bool afterDelete = await _dbContext.Players
            .AnyAsync(p => p.Id == user.Id);

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
        var allPlayers = _dbContext.Players.AsAsyncEnumerable();

        await foreach (var player in allPlayers)
        {
            player.ResetRequested = true;
        }

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();

        bool cardsExistBefore = await _dbContext.Cards.AnyAsync();
        bool decksExistBefore = await _dbContext.Decks.AnyAsync();
        bool boxesExistBefore = await _dbContext.Boxes.AnyAsync();

        await _playerManager.ResetAsync();

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
