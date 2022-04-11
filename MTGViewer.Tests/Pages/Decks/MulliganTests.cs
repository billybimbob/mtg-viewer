using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Bunit;
using Bunit.TestDoubles;
using Xunit;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Pages.Decks;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Decks;

public class MulliganTests : IAsyncLifetime
{
    private readonly IServiceProvider _services;
    private readonly UserManager<CardUser> _userManager;
    private readonly IUserClaimsPrincipalFactory<CardUser> _claimsFactory;

    private readonly TestDataGenerator _testGen;
    private readonly TestContext _testContext;

    public MulliganTests(
        IServiceProvider services,
        UserManager<CardUser> userManager,
        IUserClaimsPrincipalFactory<CardUser> claimsFactory,
        TestDataGenerator testGen)
    {
        _services = services;
        _userManager = userManager;
        _claimsFactory = claimsFactory;

        _testGen = testGen;
        _testContext = new TestContext();
    }


    public async Task InitializeAsync()
    {
        _testContext.AddFakePersistentComponentState();

        _testContext.Services.AddFallbackServiceProvider(_services);
        _testContext.Services.AddScoped<UserManager<CardUser>>((_) => _userManager);

        await _testGen.SeedAsync();
    }

    public async Task DisposeAsync()
    {
        _testContext.Dispose();

        await _testGen.ClearAsync();
    }


    [Fact]
    public async Task LoadData_NoUser_Redirect()
    {
        _testContext.AddTestAuthorization();

        var deck = await _testGen.CreateDeckAsync(numCards: 10);

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        var redirect = $"{nav.BaseUri}Cards";

        Assert.Equal(redirect, nav.Uri);
    }


    [Fact]
    public async Task LoadData_NoCards_Redirect()
    {
        var deck = await _testGen.CreateEmptyDeckAsync();

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);
        var claims = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(claims.Claims.ToArray());

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        var redirect = $"{nav.BaseUri}Decks/Details/{deck.Id}";

        Assert.Equal(redirect, nav.Uri);
    }


    [Fact]
    public async Task LoadData_InvalidDeck_Redirect()
    {
        var user = await _userManager.Users.FirstAsync();
        var claims = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(claims.Claims.ToArray());

        var invalidDeck = await _services
            .GetRequiredService<CardDbContext>().Decks
            .Where(d => d.OwnerId != user.Id)
            .Select(d => d.Id)
            .FirstAsync();

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, invalidDeck));

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        var redirect = $"{nav.BaseUri}Decks";

        Assert.Equal(redirect, nav.Uri);
    }


    [Fact]
    public async Task PickMulligan_NoneType_NoCards()
    {
        var deck = await _testGen.CreateDeckAsync(numCards: 10);

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);
        var claims = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(claims.Claims.ToArray());

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var chooseMulligan = cut.WaitForElement("select:not([disabled])");

        chooseMulligan.Change(Mulligan.MulliganType.None);

        cut.WaitForState(() => cut.FindAll("img").Count == 0);
    }


    [Fact]
    public async Task PickMulligan_BuiltType_ShowCards()
    {
        var deck = await _testGen.CreateDeckAsync(numCards: 10);

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);
        var claims = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(claims.Claims.ToArray());

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var chooseMulligan = cut.WaitForElement("select:not([disabled])");

        chooseMulligan.Change(Mulligan.MulliganType.Built);

        cut.WaitForState(() => cut.FindAll("img").Count > 0);
    }


    [Fact]
    public async Task PickMulligan_TheorycraftType_ShowCards()
    {
        var deck = await _testGen.CreateDeckAsync(numCards: 10);

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);
        var claims = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(claims.Claims.ToArray());

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var chooseMulligan = cut.WaitForElement("select:not([disabled])");

        chooseMulligan.Change(Mulligan.MulliganType.Theorycraft);

        cut.WaitForState(() => cut.FindAll("img").Count > 0);
    }


    [Fact]
    public async Task PickMulligan_DrawCard_AddCard()
    {
        var deck = await _testGen.CreateDeckAsync(numCards: 10);

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);
        var claims = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(claims.Claims.ToArray());

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var chooseMulligan = cut.WaitForElement("select:not([disabled])");

        chooseMulligan.Change(Mulligan.MulliganType.Theorycraft);

        cut.WaitForState(() => cut.FindAll("img").Count > 0);

        var drawCard = cut.Find("button[title=\"Draw a Card\"]");
        int beforeCards = cut.FindAll("img").Count;

        drawCard.Click();

        int afterCards = cut.FindAll("img").Count;

        Assert.Equal(1, afterCards - beforeCards);
    }


    [Fact]
    public async Task PickMulligan_BackButton_ClearCards()
    {
        var deck = await _testGen.CreateDeckAsync(numCards: 10);

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);
        var claims = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(claims.Claims.ToArray());

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var chooseMulligan = cut.WaitForElement("select:not([disabled])");

        chooseMulligan.Change(Mulligan.MulliganType.Theorycraft);

        cut.WaitForState(() => cut.FindAll("img").Count > 0);

        var backButton = cut.Find("button[title=\"Choose Mulligan Type\"]");

        backButton.Click();

        cut.WaitForState(() => cut.FindAll("img").Count == 0);
    }


    [Fact]
    public async Task PickMulligan_NewHand_ShowCards()
    {
        var deck = await _testGen.CreateDeckAsync(numCards: 10);

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);
        var claims = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(claims.Claims.ToArray());

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var chooseMulligan = cut.WaitForElement("select:not([disabled])");

        chooseMulligan.Change(Mulligan.MulliganType.Theorycraft);

        cut.WaitForState(() => cut.FindAll("img").Count > 0);

        var newHand = cut.Find("button[title=\"Get a New Hand\"]");

        newHand.Click();

        cut.WaitForState(() => cut.FindAll("img").Count > 0);
    }
}