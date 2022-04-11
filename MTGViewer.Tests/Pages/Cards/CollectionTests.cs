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

        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 1);
    }


    [Fact]
    public void ChangeName_InvalidName_ChangeUrl()
    {
        const string searchName = "test invalid search";

        var cut = _testContext.RenderComponent<Collection>();
        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        var search = cut.Find("input[placeholder=\"Card Name\"]");

        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 1);

        search.Change(searchName);

        Assert.Contains(Uri.EscapeDataString(searchName), nav.Uri);
    }


    [Fact]
    public void LoadData_NameParam_NoResult()
    {
        const string searchName = "test invalid search";

        var cut = _testContext.RenderComponent<Collection>(p => p
            .Add(c => c.Name, searchName));

        cut.WaitForState(() => cut.FindAll("tbody tr").Count == 1);
    }


    [Fact]
    public void ChangeType_InvalidType_ChangeUrl()
    {
        const string searchType = "test invalid type";

        var cut = _testContext.RenderComponent<Collection>();
        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        var search = cut.Find("input[placeholder=\"Card Name\"]");

        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 1);

        search.Change($"/t {searchType}");

        Assert.All(searchType.Split(), t =>
            Assert.Contains(t, nav.Uri));
    }


    [Fact]
    public void LoadData_TypesParam_NoResult()
    {
        var searchTypes = "test invalid type".Split();

        var cut = _testContext.RenderComponent<Collection>(p => p
            .Add(c => c.Types, searchTypes));

        cut.WaitForState(() => cut.FindAll("tbody tr").Count == 1);
    }


    [Fact]
    public void ChangeColor_GreenColor_ChangeUrl()
    {
        const int blue = (int)Color.Blue;

        var cut = _testContext.RenderComponent<Collection>();
        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        var greenButton = cut.Find(".ms-u");

        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 1);

        greenButton.Click();

        Assert.Contains(blue.ToString(), nav.Uri);
    }


    [Fact]
    public void LoadData_ColorParam_Success()
    {
        const int blue = (int)Color.Blue;

        var cut = _testContext.RenderComponent<Collection>(p => p
            .Add(c => c.Colors, blue));

        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 1);
    }
}