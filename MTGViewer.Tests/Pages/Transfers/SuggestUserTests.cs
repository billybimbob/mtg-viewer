using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Transfers;

public class SuggestTests : IAsyncLifetime
{
    private readonly SuggestUserModel _suggestUserModel;
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private readonly TestDataGenerator _testGen;

    public SuggestTests(
        SuggestUserModel suggestUserModel,
        CardDbContext dbContext,
        UserManager<CardUser> userManager,
        TestDataGenerator testGen)
    {
        _suggestUserModel = suggestUserModel;
        _dbContext = dbContext;
        _userManager = userManager;
        _testGen = testGen;
    }


    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();


    [Fact]
    public async Task OnPostSuggest_SameAsReceiver_NoChange()
    {
        var receiverId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync();

        var receiverCards = await _dbContext.Decks
            .Where(d => d.OwnerId == receiverId)
            .SelectMany(d => d.Cards, (_, amt) => amt.CardId)
            .ToArrayAsync();

        var cardId = await _dbContext.Cards
            .Where(c => !receiverCards.Contains(c.Id))
            .Select(c => c.Id)
            .FirstAsync();

        await _suggestUserModel.SetModelContextAsync(_userManager, receiverId);

        _suggestUserModel.Suggestion = new Suggestion
        {
            CardId = cardId,
            ReceiverId = receiverId
        };

        int suggestsBefore = await _dbContext.Suggestions.CountAsync();
        var result = await _suggestUserModel.OnPostAsync(default);
        int suggestsAfter = await _dbContext.Suggestions.CountAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(suggestsBefore, suggestsAfter);
    }


    [Fact]
    public async Task OnPostSuggest_MissingCard_NoChange()
    {
        var receiverId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync();

        var senderId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync(uid => uid != receiverId);

        await _suggestUserModel.SetModelContextAsync(_userManager, senderId);

        _suggestUserModel.Suggestion = new Suggestion
        {
            ReceiverId = receiverId
        };

        int suggestsBefore = await _dbContext.Suggestions.CountAsync();
        var result = await _suggestUserModel.OnPostAsync(default);
        int suggestsAfter = await _dbContext.Suggestions.CountAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(suggestsBefore, suggestsAfter);
    }


    [Fact]
    public async Task OnPostSuggest_MissingReceiver_NoChange()
    {
        var receiverId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync();

        var receiverCards = await _dbContext.Decks
            .Where(d => d.OwnerId == receiverId)
            .SelectMany(d => d.Cards, (_, amt) => amt.CardId)
            .ToArrayAsync();

        var cardId = await _dbContext.Cards
            .Where(c => !receiverCards.Contains(c.Id))
            .Select(c => c.Id)
            .FirstAsync();

        var senderId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync(uid => uid != receiverId);

        await _suggestUserModel.SetModelContextAsync(_userManager, senderId);

        _suggestUserModel.Suggestion = new Suggestion
        {
            CardId = cardId,
        };

        int suggestsBefore = await _dbContext.Suggestions.CountAsync();
        var result = await _suggestUserModel.OnPostAsync(default);
        int suggestsAfter = await _dbContext.Suggestions.CountAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(suggestsBefore, suggestsAfter);
    }


    [Fact]
    public async Task OnPostSuggest_WithInvalidTo_NoChange()
    {
        var target = await _dbContext.Amounts
            .Include(a => a.Location)
            .AsNoTracking()
            .FirstAsync(a => a.Location is Deck);

        var receiverId = ((Deck)target.Location).OwnerId;

        var senderId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync(uid => uid != receiverId);

        await _suggestUserModel.SetModelContextAsync(_userManager, senderId);

        _suggestUserModel.Suggestion = new Suggestion
        {
            CardId = target.CardId,
            ReceiverId = receiverId,
            ToId = target.LocationId
        };

        int suggestsBefore = await _dbContext.Suggestions.CountAsync();
        var result = await _suggestUserModel.OnPostAsync(default);
        int suggestsAfter = await _dbContext.Suggestions.CountAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(suggestsBefore, suggestsAfter);
    }


    [Fact(Skip = "Determined with model error")]
    public async Task OnPostSuggest_WithLongComment_NoChange()
    {
        string longComment = string.Join("", Enumerable.Repeat("a", 85));

        var receiverId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync();

        var receiverCards = await _dbContext.Decks
            .Where(d => d.OwnerId == receiverId)
            .SelectMany(d => d.Cards, (_, amt) => amt.CardId)
            .ToArrayAsync();

        var cardId = await _dbContext.Cards
            .Where(c => !receiverCards.Contains(c.Id))
            .Select(c => c.Id)
            .FirstAsync();

        var senderId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync(uid => uid != receiverId);

        await _suggestUserModel.SetModelContextAsync(_userManager, senderId);

        _suggestUserModel.Suggestion = new Suggestion
        {
            CardId = cardId,
            ReceiverId = receiverId,
            Comment = longComment
        };

        int suggestsBefore = await _dbContext.Suggestions.CountAsync();
        var result = await _suggestUserModel.OnPostAsync(default);
        int suggestsAfter = await _dbContext.Suggestions.CountAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(suggestsBefore, suggestsAfter);
    }


    [Fact]
    public async Task OnPostSuggest_DuplicateSuggest_NoChange()
    {
        var suggest = await _dbContext.Suggestions
            .AsNoTracking()
            .FirstAsync();

        var senderId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync(uid => uid != suggest.ReceiverId);

        await _suggestUserModel.SetModelContextAsync(_userManager, senderId);

        _suggestUserModel.Suggestion = suggest;

        int suggestsBefore = await _dbContext.Suggestions.CountAsync();
        var result = await _suggestUserModel.OnPostAsync(default);
        int suggestsAfter = await _dbContext.Suggestions.CountAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(suggestsBefore, suggestsAfter);
    }


    [Fact]
    public async Task OnPost_ValidSuggest_NewSuggestion()
    {
        var receiverId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync();

        var receiverCards = await _dbContext.Decks
            .Where(d => d.OwnerId == receiverId)
            .SelectMany(d => d.Cards, (_, amt) => amt.CardId)
            .ToArrayAsync();

        var cardId = await _dbContext.Cards
            .Where(c => !receiverCards.Contains(c.Id))
            .Select(c => c.Id)
            .FirstAsync();

        var senderId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync(uid => uid != receiverId);

        await _suggestUserModel.SetModelContextAsync(_userManager, senderId);

        _suggestUserModel.Suggestion = new Suggestion
        {
            CardId = cardId,
            ReceiverId = receiverId,
        };

        int suggestsBefore = await _dbContext.Suggestions.CountAsync();
        var result = await _suggestUserModel.OnPostAsync(default);
        int suggestsAfter = await _dbContext.Suggestions.CountAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, suggestsAfter - suggestsBefore);
    }


    [Fact]
    public async Task OnPost_WithComment_NewSuggestion()
    {
        string comment = string.Join("", Enumerable.Repeat("a", 40));

        var receiverId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync();

        var receiverCards = await _dbContext.Decks
            .Where(d => d.OwnerId == receiverId)
            .SelectMany(d => d.Cards, (_, amt) => amt.CardId)
            .ToArrayAsync();

        var cardId = await _dbContext.Cards
            .Where(c => !receiverCards.Contains(c.Id))
            .Select(c => c.Id)
            .FirstAsync();

        var senderId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync(uid => uid != receiverId);

        await _suggestUserModel.SetModelContextAsync(_userManager, senderId);

        _suggestUserModel.Suggestion = new Suggestion
        {
            CardId = cardId,
            ReceiverId = receiverId,
            Comment = comment
        };

        int suggestsBefore = await _dbContext.Suggestions.CountAsync();
        var result = await _suggestUserModel.OnPostAsync(default);
        int suggestsAfter = await _dbContext.Suggestions.CountAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, suggestsAfter - suggestsBefore);
    }


    [Fact]
    public async Task OnPost_WithTo_NewSuggestion()
    {
        var target = await _dbContext.Amounts
            .Include(a => a.Location)
            .AsNoTracking()
            .FirstAsync(a => a.Location is Deck);

        var receiverId = (target.Location as Deck)!.OwnerId;

        var receiverCards = await _dbContext.Decks
            .Where(d => d.OwnerId == receiverId)
            .SelectMany(d => d.Cards, (_, amt) => amt.CardId)
            .ToArrayAsync();

        var cardId = await _dbContext.Cards
            .Where(c => !receiverCards.Contains(c.Id))
            .Select(c => c.Id)
            .FirstAsync();

        var senderId = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync(uid => uid != receiverId);

        await _suggestUserModel.SetModelContextAsync(_userManager, senderId);

        _suggestUserModel.Suggestion = new Suggestion
        {
            CardId = cardId,
            ReceiverId = receiverId,
            ToId = target.LocationId
        };

        int suggestsBefore = await _dbContext.Suggestions.CountAsync();
        var result = await _suggestUserModel.OnPostAsync(default);
        int suggestsAfter = await _dbContext.Suggestions.CountAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, suggestsAfter - suggestsBefore);
    }
}