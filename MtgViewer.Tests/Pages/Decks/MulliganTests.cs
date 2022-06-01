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
using MtgViewer.Services;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Pages.Decks;

public sealed class MulliganTests : IAsyncLifetime, IDisposable
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
        _testContext.Services.AddScoped(_ => _userManager);

        _testContext.Services.Configure<MulliganOptions>(options =>
        {
            options.DrawInterval = 0;
        });

        await _testGen.SeedAsync();
    }

    public async Task DisposeAsync()
    {
        _testContext.Dispose();

        await _testGen.ClearAsync();
    }

    void IDisposable.Dispose() => _testContext.Dispose();

    [Fact]
    public async Task LoadData_NoUser_Redirect()
    {
        _testContext.AddTestAuthorization();

        var deck = await _testGen.CreateDeckAsync(numCards: 10);

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        string redirect = $"{nav.BaseUri}Cards";

        Assert.Equal(redirect, nav.Uri);
    }

    [Fact]
    public async Task LoadData_NoCards_Redirect()
    {
        var deck = await _testGen.CreateEmptyDeckAsync();

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);
        var identity = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(identity.Claims.ToArray());

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        string redirect = $"{nav.BaseUri}Decks/Details/{deck.Id}";

        Assert.Equal(redirect, nav.Uri);
    }

    [Fact]
    public async Task LoadData_InvalidDeck_Redirect()
    {
        var user = await _userManager.Users.FirstAsync();
        var identity = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(identity.Claims.ToArray());

        int invalidDeck = await _services
            .GetRequiredService<CardDbContext>().Decks
            .Where(d => d.OwnerId != user.Id)
            .Select(d => d.Id)
            .FirstAsync();

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, invalidDeck));

        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        string redirect = $"{nav.BaseUri}Decks";

        Assert.Equal(redirect, nav.Uri);
    }

    private async Task<Deck> AddDeckAndSameUserAsync()
    {
        var deck = await _testGen.CreateDeckAsync(numCards: 10);

        var user = await _userManager.Users.FirstAsync(u => u.Id == deck.OwnerId);
        var identity = await _claimsFactory.CreateAsync(user);

        var auth = _testContext.AddTestAuthorization();

        auth.SetClaims(identity.Claims.ToArray());

        return deck;
    }

    [Fact]
    public async Task PickMulligan_NoneType_NoCards()
    {
        var deck = await AddDeckAndSameUserAsync();

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var chooseMulligan = cut.Find("select:not([disabled])");

        chooseMulligan.Change(DeckMulligan.None);

        var images = cut.FindAll("img");

        Assert.Equal(0, images.Count);
    }

    [Fact]
    public async Task PickMulligan_BuiltType_ShowCards()
    {
        var deck = await AddDeckAndSameUserAsync();

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var chooseMulligan = cut.Find("select:not([disabled])");

        chooseMulligan.Change(DeckMulligan.Built);

        var images = cut.FindAll("img");

        Assert.True(images.Count > 0);
    }

    [Fact]
    public async Task PickMulligan_TheorycraftType_ShowCards()
    {
        var deck = await AddDeckAndSameUserAsync();

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var chooseMulligan = cut.Find("select:not([disabled])");

        chooseMulligan.Change(DeckMulligan.Theorycraft);

        var images = cut.FindAll("img");

        Assert.True(images.Count > 0);
    }

    [Fact]
    public async Task PickMulligan_DrawCard_AddCard()
    {
        var deck = await AddDeckAndSameUserAsync();

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var chooseMulligan = cut.Find("select:not([disabled])");

        chooseMulligan.Change(DeckMulligan.Theorycraft);

        var drawCard = cut.Find("button[title=\"Draw a Card\"]");
        int beforeCards = cut.FindAll("img").Count;

        drawCard.Click();

        int afterCards = cut.FindAll("img").Count;

        Assert.Equal(1, afterCards - beforeCards);
    }

    [Fact]
    public async Task PickMulligan_BackButton_ClearCards()
    {
        var deck = await AddDeckAndSameUserAsync();

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var chooseMulligan = cut.Find("select:not([disabled])");

        chooseMulligan.Change(DeckMulligan.Theorycraft);

        var backButton = cut.Find("button[title=\"Choose Mulligan Type\"]");
        var beforeImages = cut.FindAll("img");

        backButton.Click();

        var afterImages = cut.FindAll("img");

        Assert.True(beforeImages.Count > 0);
        Assert.Equal(0, afterImages.Count);
    }

    [Fact]
    public async Task PickMulligan_NewHand_ShowCards()
    {
        var deck = await AddDeckAndSameUserAsync();

        var cut = _testContext.RenderComponent<Mulligan>(p => p
            .Add(m => m.DeckId, deck.Id));

        var chooseMulligan = cut.Find("select:not([disabled])");

        chooseMulligan.Change(DeckMulligan.Theorycraft);

        var newHand = cut.Find("button[title=\"Get a New Hand\"]:not([disabled])");

        var beforeImages = cut.FindAll("img");

        newHand.Click();

        var afterImages = cut.FindAll("img");

        Assert.True(beforeImages.Count > 0);
        Assert.True(afterImages.Count > 0);
    }
}
