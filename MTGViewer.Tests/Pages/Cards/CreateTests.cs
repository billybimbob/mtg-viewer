using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Bunit;
using Bunit.TestDoubles;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Cards;
using MTGViewer.Services;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Cards;

public class CreateTests : IAsyncLifetime
{
    private readonly IServiceProvider _services;

    private readonly CardDbContext _dbContext;
    private readonly TestMtgApiQuery _mtgQuery;

    private readonly TestDataGenerator _testGen;
    private readonly TestContext _testContext;

    public CreateTests(
        IServiceProvider serviceProvider,
        CardDbContext dbContext,
        TestMtgApiQuery testMtgQuery,
        TestDataGenerator testDataGenerator)
    {
        _services = serviceProvider;

        _dbContext = dbContext;
        _mtgQuery = testMtgQuery;

        _testGen = testDataGenerator;
        _testContext = new TestContext();
    }

    public async Task InitializeAsync()
    {
        _testContext.AddFakePersistentComponentState();
        _testContext.AddTestAuthorization();

        _testContext.Services.AddScoped<IMTGQuery, TestMtgApiQuery>(_ => _mtgQuery);
        _testContext.Services.AddFallbackServiceProvider(_services);

        await _testGen.SeedAsync();
    }

    public async Task DisposeAsync()
    {
        _testContext.Dispose();

        await _testGen.ClearAsync();
    }

    [Fact]
    public void LoadData_NoParamters_CardSearchForm()
    {
        var cut = _testContext.RenderComponent<Create>();

        var forms = cut.FindAll("form");

        Assert.Equal(1, forms.Count);
    }

    [Fact]
    public async Task LoadData_NameParameter_ShowResults()
    {
        var cardName = await _mtgQuery.SourceCards
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
    public async Task Submit_SearchName_Redirect()
    {
        var cardName = await _mtgQuery.SourceCards
            .Select(c => c.Name)
            .FirstAsync();

        var cut = _testContext.RenderComponent<Create>();

        var nav = cut.Services.GetRequiredService<FakeNavigationManager>();

        var nameInput = cut.Find("input[placeholder=\"Name\"]");

        nameInput.Change(cardName);

        var form = cut.FindComponent<EditForm>();

        await cut.InvokeAsync(() => form.Instance.OnValidSubmit.InvokeAsync());

        Assert.Contains(Uri.EscapeDataString(cardName), nav.Uri);
    }

    [Theory]
    [InlineData(Color.Red)]
    [InlineData(Color.Red | Color.White)]
    [InlineData(Color.Black | Color.Blue | Color.Green)]
    public async Task Submit_SearchColor_Redirect(Color color)
    {
        var cut = _testContext.RenderComponent<Create>();

        var nav = cut.Services.GetRequiredService<FakeNavigationManager>();

        ClickColorButtons(cut, color);

        var form = cut.FindComponent<EditForm>();

        var invariant = CultureInfo.InvariantCulture;

        await cut.InvokeAsync(() => form.Instance.OnValidSubmit.InvokeAsync());

        string colorName = ((int)color).ToString(invariant);

        Assert.Contains(colorName, nav.Uri);
    }

    private static void ClickColorButtons(IRenderedComponent<Create> component, Color colors)
    {
        foreach (var color in Symbol.Colors.Keys)
        {
            if (colors.HasFlag(color))
            {
                var button = component.Find($"button[title=\"Toggle {color}\"]");

                button.Click();
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public async Task Submit_SearchCmc_Redirect(int? cmc)
    {
        var cut = _testContext.RenderComponent<Create>();

        var nav = cut.Services.GetRequiredService<FakeNavigationManager>();

        var cmcInput = cut.Find("input[placeholder=\"Mana Value\"]");

        cmcInput.Change(cmc);

        var form = cut.FindComponent<EditForm>();

        var invariant = CultureInfo.InvariantCulture;

        await cut.InvokeAsync(() => form.Instance.OnValidSubmit.InvokeAsync());

        Assert.Contains(cmc?.ToString(invariant) ?? string.Empty, nav.Uri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Rarity.Common)]
    [InlineData(Rarity.Rare)]
    public async Task Submit_SearchRarity_Redirect(Rarity? rarity)
    {
        var cut = _testContext.RenderComponent<Create>();

        var nav = cut.Services.GetRequiredService<FakeNavigationManager>();

        var rarityInput = cut.Find("select[title=\"Choose Rarity\"]");

        rarityInput.Change(rarity);

        var form = cut.FindComponent<EditForm>();

        var invariant = CultureInfo.InvariantCulture;

        await cut.InvokeAsync(() => form.Instance.OnValidSubmit.InvokeAsync());

        string rarityName = ((int?)rarity)?.ToString(invariant) ?? string.Empty;

        Assert.Contains(rarityName, nav.Uri);
    }

    [Fact]
    public async Task AddCards_MultipleCopies_NewCards()
    {
        const int addedCopies = 7;

        var cardName = await _mtgQuery.SourceCards
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
        var cardName = await _mtgQuery.SourceCards
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
