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
using MtgViewer.Pages.Decks;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Pages.Decks;

public sealed class CraftTests : IAsyncLifetime, IDisposable
{
    private readonly IServiceProvider _services;

    private readonly UserManager<CardUser> _userManager;
    private readonly IUserClaimsPrincipalFactory<CardUser> _claimsFactory;

    private readonly CardDbContext _dbContext;

    private readonly ActionHandlerFactory _handlerFactory;
    private readonly TestDataGenerator _testGen;

    private readonly TestContext _testContext;

    public CraftTests(
        IServiceProvider serviceProvider,
        UserManager<CardUser> userManager,
        IUserClaimsPrincipalFactory<CardUser> claimFactory,
        CardDbContext dbContext,
        ActionHandlerFactory handlerFactory,
        TestDataGenerator testDataGenerator)
    {
        _services = serviceProvider;

        _userManager = userManager;
        _claimsFactory = claimFactory;

        _dbContext = dbContext;

        _handlerFactory = handlerFactory;
        _testGen = testDataGenerator;

        _testContext = new TestContext();
    }

    public async Task InitializeAsync()
    {
        _handlerFactory.AddRouteDataContext<Craft>();

        _testContext.AddFakePersistentComponentState();

        _testContext.Services.AddScoped(_ => _userManager);
        _testContext.Services.AddFallbackServiceProvider(_services);

        await _testGen.SeedAsync();
    }

    public async Task DisposeAsync()
    {
        _testContext.Dispose();

        await _testGen.ClearAsync();
    }

    void IDisposable.Dispose() => _testContext.Dispose();

    [Fact]
    public void LoadData_NoUserNewDeck_Redirect()
    {
        _testContext.AddTestAuthorization();

        _ = _testContext.RenderComponent<Craft>();

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        Assert.EndsWith("/Decks", nav.Uri);
    }

    [Fact]
    public async Task LoadData_NoUserEditDeck_Redirect()
    {
        _testContext.AddTestAuthorization();

        var deck = await _dbContext.Decks.FirstAsync();

        var cut = _testContext.RenderComponent<Craft>(p => p
            .Add(c => c.DeckId, deck.Id));

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        Assert.EndsWith("/Decks", nav.Uri);
    }

    [Fact]
    public async Task LoadData_WrongUser_Redirect()
    {
        var user = await _userManager.Users.FirstAsync();
        var identity = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(identity.Claims.ToArray());

        int invalidDeck = await _services
            .GetRequiredService<CardDbContext>()
            .Decks
            .Where(d => d.OwnerId != user.Id)
            .Select(d => d.Id)
            .FirstAsync();

        var cut = _testContext.RenderComponent<Craft>(p => p
            .Add(c => c.DeckId, invalidDeck));

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        var uri = new Uri(nav.Uri);

        Assert.Equal(string.Empty, uri.Query);
    }

    [Fact]
    public async Task LoadData_NewDeck_Success()
    {
        var user = await _userManager.Users.FirstAsync();

        var identity = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(identity.Claims.ToArray());

        _testContext.AddTestAuthorization();

        var cut = _testContext.RenderComponent<Craft>();

        var pickMulligans = cut.FindAll("select[title=\"Deck Build Type\"]");

        Assert.Equal(1, pickMulligans.Count);
    }

    [Fact]
    public async Task LoadData_EditDeck_Success()
    {
        var deck = await _dbContext.Decks.FirstAsync();

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);

        var identity = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(identity.Claims.ToArray());

        _testContext.AddTestAuthorization();

        var cut = _testContext.RenderComponent<Craft>();

        var pickMulligans = cut.FindAll("select[title=\"Deck Build Type\"]");

        Assert.Equal(1, pickMulligans.Count);
    }

    private async Task<Deck> AddDeckAndSameUserAsync(int numCards)
    {
        var deck = await _testGen.CreateDeckAsync(numCards);

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);
        var identity = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(identity.Claims.ToArray());

        return deck;
    }

    private static void ClickButton(IRenderedComponent<Craft> cut, string buttonCss, int numClicks)
    {
        for (int i = 0; i < numClicks; ++i)
        {
            var button = cut.WaitForElement(buttonCss);

            button.Click();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public async Task SaveData_AddReturns_NewReturns(int numClicks)
    {
        var deck = await AddDeckAndSameUserAsync(numClicks);

        var cut = _testContext.RenderComponent<Craft>(p => p
            .Add(c => c.DeckId, deck.Id));

        ClickButton(cut, "button[title~=\"Remove\"]:not([disabled])", numClicks);

        int oldReturns = await _dbContext.Givebacks.SumAsync(g => g.Copies);

        var saveButton = cut.WaitForElement("button[title=\"Save Deck\"]:not([disabled])");

        saveButton.Click();

        int newReturns = await _dbContext.Givebacks.SumAsync(g => g.Copies);

        Assert.Equal(numClicks, newReturns - oldReturns);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public async Task SaveData_RemoveReturns_LessReturns(int numClicks)
    {
        var deck = await _testGen.CreateReturnDeckAsync(numClicks);

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);
        var identity = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(identity.Claims.ToArray());

        var cut = _testContext.RenderComponent<Craft>(p => p
            .Add(c => c.DeckId, deck.Id));

        ClickButton(cut, "button[title^=\"Add Back\"]", numClicks);

        int oldReturns = await _dbContext.Givebacks.SumAsync(g => g.Copies);

        var saveButton = cut.WaitForElement("button[title=\"Save Deck\"]:not([disabled])");

        saveButton.Click();

        int newReturns = await _dbContext.Givebacks.SumAsync(g => g.Copies);

        Assert.Equal(numClicks, oldReturns - newReturns);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public async Task SaveData_AddWants_NewWants(int numClicks)
    {
        var deck = await AddDeckAndSameUserAsync(numClicks);

        var cut = _testContext.RenderComponent<Craft>(p => p
            .Add(c => c.DeckId, deck.Id));

        var selectBuild = cut.Find("select[title=\"Deck Build Type\"]");

        selectBuild.Change(DeckCraft.Theorycraft);

        ClickButton(cut, "button[title~=\"Add\"]:not([disabled])", numClicks);

        int oldWants = await _dbContext.Wants.SumAsync(g => g.Copies);

        var saveButton = cut.WaitForElement("button[title=\"Save Deck\"]:not([disabled])");

        saveButton.Click();

        int newWants = await _dbContext.Wants.SumAsync(g => g.Copies);

        Assert.Equal(numClicks, newWants - oldWants);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public async Task SaveData_RemoveWants_LessWants(int numClicks)
    {
        var deck = await _testGen.CreateWantDeckAsync(numClicks);

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);
        var identity = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(identity.Claims.ToArray());

        var cut = _testContext.RenderComponent<Craft>(p => p
            .Add(c => c.DeckId, deck.Id));

        var selectBuild = cut.Find("select[title=\"Deck Build Type\"]");

        selectBuild.Change(DeckCraft.Theorycraft);

        ClickButton(cut, "button[title~=\"Remove\"]", numClicks);

        int oldWants = await _dbContext.Wants.SumAsync(g => g.Copies);

        var saveButton = cut.WaitForElement("button[title=\"Save Deck\"]:not([disabled])");

        saveButton.Click();

        int newWants = await _dbContext.Wants.SumAsync(w => w.Copies);

        Assert.Equal(numClicks, oldWants - newWants);
    }

    // TODO: try to add concurrent tests
}
