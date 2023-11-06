using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Bunit;
using Bunit.TestDoubles;
using Xunit;

using MtgViewer.Data;
using MtgViewer.Pages.Cards;
using MtgViewer.Services.Search;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Pages.Cards;

public sealed class CreateTests : IAsyncLifetime, IDisposable
{
    private readonly IServiceProvider _services;

    private readonly CardDbContext _dbContext;
    private readonly TestMtgApiQuery _mtgQuery;

    private readonly TestDataGenerator _testGen;
    private readonly ActionHandlerFactory _handlerFactory;
    private readonly TestContext _testContext;

    public CreateTests(
        IServiceProvider serviceProvider,
        CardDbContext dbContext,
        TestMtgApiQuery mtgQuery,
        TestDataGenerator testDataGenerator,
        ActionHandlerFactory handlerFactory)
    {
        _services = serviceProvider;

        _dbContext = dbContext;
        _mtgQuery = mtgQuery;

        _testGen = testDataGenerator;
        _handlerFactory = handlerFactory;

        _testContext = new TestContext();
    }

    public async Task InitializeAsync()
    {
        _handlerFactory.AddRouteDataContext<Create>();

        _testContext.AddFakePersistentComponentState();
        _testContext.AddTestAuthorization();

        _testContext.Services.AddScoped<IMtgQuery, TestMtgApiQuery>(_ => _mtgQuery);
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
    public void LoadData_NoParameters_CardSearchForm()
    {
        var cut = _testContext.RenderComponent<Create>();

        var forms = cut.FindAll("form");

        Assert.Equal(1, forms.Count);
    }

    [Fact]
    public async Task LoadData_NameParameter_ShowResults()
    {
        string cardName = await _mtgQuery.SourceCards
            .Select(c => c.Name)
            .FirstAsync();

        var cut = _testContext.RenderComponent<Create>(p => p
            .Add(c => c.Name, cardName));

        var cardEntries = cut.FindAll("tbody tr");

        Assert.True(cardEntries.Count > 0);
    }

    [Theory]
    [InlineData((int)Color.Red)]
    [InlineData((int)Color.White)]
    [InlineData((int)(Color.Blue | Color.Red))]
    [InlineData(10)]
    [InlineData(-5)]
    public void LoadData_ColorParameter_ShowResults(int color)
    {
        var cut = _testContext.RenderComponent<Create>(p => p
            .Add(c => c.Colors, color));

        var tables = cut.FindAll("table");

        Assert.Equal(1, tables.Count);
    }

    [Fact]
    public void LoadData_InvalidColorParameter_CardSearchForm()
    {
        const int color = (int)Color.None;

        var cut = _testContext.RenderComponent<Create>(p => p
            .Add(c => c.Colors, color));

        var forms = cut.FindAll("form");

        Assert.Equal(1, forms.Count);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void LoadData_CmcParameter_ShowResults(int? cmc)
    {
        var cut = _testContext.RenderComponent<Create>(p => p
            .Add(c => c.Cmc, cmc));

        var tables = cut.FindAll("table");

        Assert.Equal(1, tables.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-2)]
    public void LoadData_InvalidCmcParameter_CardSearchForm(int? cmc)
    {
        var cut = _testContext.RenderComponent<Create>(p => p
            .Add(c => c.Cmc, cmc));

        var forms = cut.FindAll("form");

        Assert.Equal(1, forms.Count);
    }

    [Theory]
    [InlineData((int)Rarity.Common)]
    [InlineData((int)Rarity.Rare)]
    public void LoadData_RarityParameter_ShowResults(int rarity)
    {
        var cut = _testContext.RenderComponent<Create>(p => p
            .Add(c => c.Rarity, rarity));

        var tables = cut.FindAll("table");

        Assert.Equal(1, tables.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(10)]
    [InlineData(-25)]
    public void LoadData_InvalidRarityParameter_CardSearchForm(int? rarity)
    {
        var cut = _testContext.RenderComponent<Create>(p => p
            .Add(c => c.Rarity, rarity));

        var forms = cut.FindAll("form");

        Assert.Equal(1, forms.Count);
    }

    [Fact]
    public async Task AddCards_MultipleCopies_NewCards()
    {
        const int addedCopies = 7;

        string cardName = await _mtgQuery.SourceCards
            .Select(c => c.Name)
            .FirstAsync();

        int oldCopies = await _dbContext.Holds.SumAsync(h => h.Copies);

        var cut = _testContext.RenderComponent<Create>(p => p
            .Add(c => c.Name, cardName));

        var match = cut.Find("tbody tr input");

        match.Change(addedCopies);

        var addButton = cut.Find("button[title=\"Add Selected Cards\"]");

        addButton.Click();

        int newCopies = await _dbContext.Holds.SumAsync(h => h.Copies);

        var nav = cut.Services.GetRequiredService<FakeNavigationManager>();

        var uri = new Uri(nav.Uri);

        Assert.Equal(string.Empty, uri.Query);
        Assert.Equal(addedCopies, newCopies - oldCopies);
    }

    [Fact]
    public async Task Reset_PressReset_Redirect()
    {
        string cardName = await _mtgQuery.SourceCards
            .Select(c => c.Name)
            .FirstAsync();

        var cut = _testContext.RenderComponent<Create>(p => p
            .Add(c => c.Name, cardName));

        var reset = cut.Find("button[title=\"Reset Search Arguments\"]");

        reset.Click();

        var nav = cut.Services.GetRequiredService<FakeNavigationManager>();

        var uri = new Uri(nav.Uri);

        Assert.Equal(string.Empty, uri.Query);
    }
}
