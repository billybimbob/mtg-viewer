using System;
using System.Threading.Tasks;
using Bunit;
using Bunit.TestDoubles;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using MTGViewer.Data;
using MTGViewer.Pages.Cards;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Cards;

public class CollectionTests : IAsyncLifetime
{
    private readonly IServiceProvider _services;
    private readonly TestDataGenerator _testGen;
    private readonly TestContext _testContext;

    public CollectionTests(IServiceProvider services, TestDataGenerator testGen)
    {
        _services = services;
        _testGen = testGen;
        _testContext = new TestContext();
    }

    public async Task InitializeAsync()
    {
        _testContext.Services.AddFallbackServiceProvider(_services);

        _testContext.AddFakePersistentComponentState();
        _testContext.AddTestAuthorization();

        await _testGen.SeedAsync();
    }

    public async Task DisposeAsync()
    {
        _testContext.Dispose();

        await _testGen.ClearAsync();
    }

    [Fact]
    public void LoadData_NoParameters_Success()
    {
        var cut = _testContext.RenderComponent<Collection>();

        var cardEntries = cut.FindAll("tbody tr");

        Assert.True(cardEntries.Count > 1);
    }

    [Fact]
    public void ChangeName_InvalidName_ChangeUrl()
    {
        const string searchName = "test invalid search";

        var cut = _testContext.RenderComponent<Collection>();
        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        var search = cut.Find("input[placeholder=\"Card Name\"]");

        search.Change(searchName);

        Assert.Contains(Uri.EscapeDataString(searchName), nav.Uri);
    }

    [Fact]
    public void LoadData_NameParam_NoResult()
    {
        const string searchName = "test invalid search";

        var cut = _testContext.RenderComponent<Collection>(p => p
            .Add(c => c.Name, searchName));

        var cardEntries = cut.FindAll("tbody tr");

        Assert.Equal(1, cardEntries.Count);
    }

    [Fact]
    public void ChangeType_InvalidType_ChangeUrl()
    {
        const string searchType = "test invalid type";

        var cut = _testContext.RenderComponent<Collection>();
        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        var search = cut.Find("input[placeholder=\"Card Name\"]");

        search.Change($"/t {searchType}");

        Assert.All(searchType.Split(), t =>
            Assert.Contains(t, nav.Uri));
    }

    [Fact]
    public void LoadData_TypesParamater_NoResult()
    {
        var searchTypes = new[] { "test", "invalid", "type" };

        var cut = _testContext.RenderComponent<Collection>(p => p
            .Add(c => c.Types, searchTypes));

        var cardEntries = cut.FindAll("tbody tr");

        Assert.Equal(1, cardEntries.Count);
    }

    [Fact]
    public void ChangeColor_BlueColor_ChangeUrl()
    {
        const int blue = (int)Color.Blue;

        var cut = _testContext.RenderComponent<Collection>();
        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        var greenButton = cut.Find(".ms-u");

        greenButton.Click();

        Assert.Contains(blue.ToString(), nav.Uri);
    }

    [Theory]
    [InlineData((int)Color.None)]
    [InlineData((int)Color.Blue)]
    [InlineData((int)Color.White)]
    [InlineData(10)]
    [InlineData(-3)]
    public void LoadData_ColorParameter_Success(int color)
    {
        var cut = _testContext.RenderComponent<Collection>(p => p
            .Add(c => c.Colors, color));

        var inputs = cut.FindAll("input:not([disabled])");

        Assert.Equal(1, inputs.Count);
    }
}
