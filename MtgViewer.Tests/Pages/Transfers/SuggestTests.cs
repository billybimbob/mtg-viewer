using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Bunit;
using Bunit.TestDoubles;
using Xunit;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Pages.Transfers;
using MtgViewer.Services;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Pages.Transfers;

public sealed class SuggestTests : IAsyncLifetime, IDisposable
{
    private readonly IServiceProvider _services;

    private readonly UserManager<CardUser> _userManager;
    private readonly IUserClaimsPrincipalFactory<CardUser> _claimsFactory;
    private string _userId;

    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSizes;

    private readonly ActionHandlerFactory _actionFactory;
    private readonly TestDataGenerator _testGen;
    private readonly TestContext _testContext;

    public SuggestTests(
        IServiceProvider services,
        UserManager<CardUser> userManager,
        IUserClaimsPrincipalFactory<CardUser> claimsFactory,
        CardDbContext dbContext,
        PageSize pageSizes,
        ActionHandlerFactory actionFactory,
        TestDataGenerator testGen)
    {
        _services = services;

        _userManager = userManager;
        _claimsFactory = claimsFactory;
        _userId = string.Empty;

        _dbContext = dbContext;
        _pageSizes = pageSizes;

        _actionFactory = actionFactory;
        _testGen = testGen;
        _testContext = new TestContext();
    }

    public async Task InitializeAsync()
    {
        _actionFactory.AddRouteDataContext<Suggest>();
        _testContext.AddFakePersistentComponentState();

        _testContext.Services.AddScoped(_ => _userManager);
        _testContext.Services.AddFallbackServiceProvider(_services);

        await _testGen.SeedAsync();

        var user = await _userManager.Users.FirstAsync();
        var identity = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(identity.Claims.ToArray());

        _userId = user.Id;
    }

    public async Task DisposeAsync()
    {
        _testContext.Dispose();

        await _testGen.ClearAsync();
    }

    void IDisposable.Dispose() => _testContext.Dispose();

    [Fact]
    public void InitialLoad_NullCard_Redirect()
    {
        const string? nullCard = null;

        var cut = _testContext.RenderComponent<Suggest>(p => p
            .Add(s => s.CardId, nullCard));

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        Assert.DoesNotContain("/Suggest", nav.Uri);
    }

    [Fact]
    public void InitialLoad_InvalidCardId_Redirect()
    {
        const string invalidCard = "invalidCard";

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        nav.NavigateTo($"/Suggest/{invalidCard}");

        var cut = _testContext.RenderComponent<Suggest>(p => p
            .Add(s => s.CardId, invalidCard));

        Assert.DoesNotContain("/Suggest", nav.Uri);
    }

    [Fact]
    public async Task InitialLoad_ValidCardId_SuggestPage()
    {
        var card = await _dbContext.Cards.FirstAsync();

        var cut = _testContext.RenderComponent<Suggest>(p => p
            .Add(s => s.CardId, card.Id));

        var title = cut.Find("h1");

        Assert.Contains(card.Name, title.TextContent.Trim());
    }

    [Fact]
    public async Task InitialLoad_SameReceiverId_Redirect()
    {
        var card = await _dbContext.Cards.FirstAsync();

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        nav.NavigateTo($"/Suggest/{card.Id}");

        var cut = _testContext.RenderComponent<Suggest>(p => p
            .Add(s => s.CardId, card.Id)
            .Add(s => s.ReceiverId, _userId));

        Assert.Contains(card.Id, nav.Uri);
        Assert.DoesNotContain(_userId, nav.Uri);
    }

    [Fact]
    public async Task InitialLoad_InvalidReceiverId_Redirect()
    {
        const string invalidReceiver = "invalidReceiver";

        var card = await _dbContext.Cards.FirstAsync();

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        nav.NavigateTo($"/Suggest/{card.Id}?ReceiverId={invalidReceiver}");

        var cut = _testContext.RenderComponent<Suggest>(p => p
            .Add(s => s.CardId, card.Id)
            .Add(s => s.ReceiverId, invalidReceiver));

        Assert.Contains(card.Id, nav.Uri);
        Assert.DoesNotContain(invalidReceiver, nav.Uri);
    }

    [Fact]
    public async Task PickUser_ValidUser_ShowDecks()
    {
        var card = await _dbContext.Cards.FirstAsync();

        var receiver = await _dbContext.Users
            .Where(u => u.Id != _userId)
            .FirstAsync();

        var cut = _testContext.RenderComponent<Suggest>(p => p
            .Add(s => s.CardId, card.Id));

        var chooseUser = cut.WaitForElement("select[title=\"Suggest to User\"]:not([disabled])");

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        chooseUser.Change(receiver.Id);

        Assert.Contains(receiver.Id, nav.Uri);
    }

    [Fact]
    public async Task InitialLoad_ValidReceiver_ShowDecks()
    {
        var card = await _dbContext.Cards.FirstAsync();

        var receiver = await _dbContext.Users
            .Where(u => u.Id != _userId)
            .FirstAsync();

        var cut = _testContext.RenderComponent<Suggest>(p => p
            .Add(s => s.CardId, card.Id)
            .Add(s => s.ReceiverId, receiver.Id));

        string title = cut.Find("h1").TextContent.Trim();
        var deckButtons = cut.WaitForElements("button.list-group-item");

        Assert.Contains(card.Name, title);
        Assert.Contains(receiver.Name, title);

        Assert.True(deckButtons.Count > 0);
    }

    [Fact]
    public async Task LoadMoreDecks_MissingDecks_DecksAdded()
    {
        var card = await _dbContext.Cards.FirstAsync();

        var receiver = await _dbContext.Users
            .Where(u => u.Id != _userId)
            .FirstAsync();

        await AddExtraReceiverDecksAsync(receiver);

        var cut = _testContext.RenderComponent<Suggest>(p => p
            .Add(s => s.CardId, card.Id)
            .Add(s => s.ReceiverId, receiver.Id));

        string title = cut.Find("h1").TextContent.Trim();
        int itemsBefore = cut.WaitForElements("button.list-group-item").Count;

        var loadMoreDecks = cut.WaitForElement("button[title=\"Load More Decks\"]:not([disabled])");

        loadMoreDecks.Click();

        int itemsAfter = cut.WaitForElements("button.list-group-item").Count;

        Assert.Contains(card.Name, title);
        Assert.Contains(receiver.Name, title);

        Assert.True(itemsAfter > itemsBefore);
    }

    private async Task AddExtraReceiverDecksAsync(UserRef receiver)
    {
        int receiverDecks = await _dbContext.Decks
            .Where(d => d.OwnerId == receiver.Id)
            .CountAsync();

        int amountMissing = Math.Max(_pageSizes.Current - receiverDecks, 0) + 5;

        var newDecks = Enumerable
            .Range(0, amountMissing)
            .Select(_ => new Deck
            {
                Name = "Suggest Deck",
                Owner = receiver
            });

        _dbContext.Decks.AddRange(newDecks);

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task SendSuggestion_NoOptional_NewSuggestion()
    {
        var card = await _dbContext.Cards.FirstAsync();

        var receiver = await _dbContext.Users
            .Where(u => u.Id != _userId)
            .FirstAsync();

        var cut = _testContext.RenderComponent<Suggest>(p => p
            .Add(s => s.CardId, card.Id)
            .Add(s => s.ReceiverId, receiver.Id));

        var suggestForm = cut.WaitForElement("form");

        int suggestionsBefore = await _dbContext.Suggestions.CountAsync();

        await suggestForm.SubmitAsync();

        int suggestionsAfter = await _dbContext.Suggestions.CountAsync();

        Assert.Equal(1, suggestionsAfter - suggestionsBefore);
    }

    [Fact]
    public async Task SendSuggestion_OptionalSet_NewSuggestion()
    {
        var card = await _dbContext.Cards.FirstAsync();

        var receiver = await _dbContext.Users
            .Where(u => u.Id != _userId)
            .FirstAsync();

        await AddReceiverDeckAsync(receiver);

        var cut = _testContext.RenderComponent<Suggest>(p => p
            .Add(s => s.CardId, card.Id)
            .Add(s => s.ReceiverId, receiver.Id));

        var deckOption = cut.WaitForElement("button.list-group-item");

        deckOption.Click();

        var commentInput = cut.WaitForElement($"textarea#{SuggestionDto.PropertyId(s => s.Comment)}");

        commentInput.Change("This is an added comment to the suggestion");

        var suggestForm = cut.WaitForElement("form");

        int suggestionsBefore = await _dbContext.Suggestions.CountAsync();

        await suggestForm.SubmitAsync();

        int suggestionsAfter = await _dbContext.Suggestions.CountAsync();

        Assert.Equal(1, suggestionsAfter - suggestionsBefore);
    }

    private async Task AddReceiverDeckAsync(UserRef receiver)
    {
        _dbContext.Decks.Add(new Deck
        {
            Name = "Suggest Deck",
            Owner = receiver
        });

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();
    }
}
