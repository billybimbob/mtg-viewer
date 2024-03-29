using System;
using System.Globalization;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Bunit;
using Bunit.TestDoubles;
using Xunit;

using MtgViewer.Data;
using MtgViewer.Pages.Cards;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Pages.Cards;

public sealed class CollectionTests : IAsyncLifetime, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ActionHandlerFactory _handlerFactory;
    private readonly TestDataGenerator _testGen;
    private readonly TestContext _testContext;

    public CollectionTests(
        IServiceProvider services,
        ActionHandlerFactory handlerFactory,
        TestDataGenerator testGen)
    {
        _services = services;
        _handlerFactory = handlerFactory;
        _testGen = testGen;
        _testContext = new TestContext();
    }

    public async Task InitializeAsync()
    {
        _handlerFactory.AddRouteDataContext<Collection>();

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

    void IDisposable.Dispose() => _testContext.Dispose();

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
    public void LoadData_SearchNameParameter_NoResult()
    {
        const string searchName = "test invalid search";

        var cut = _testContext.RenderComponent<Collection>(p => p
            .Add(c => c.Search, searchName));

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

        Assert.Contains(Uri.EscapeDataString(searchType), nav.Uri);
    }

    [Fact]
    public void LoadData_SearchTypesParameter_NoResult()
    {
        const string searchTypes = "/t test invalid type";

        var cut = _testContext.RenderComponent<Collection>(p => p
            .Add(c => c.Search, searchTypes));

        var cardEntries = cut.FindAll("tbody tr");

        Assert.Equal(1, cardEntries.Count);
    }

    [Fact]
    public void ChangeColor_BlueColor_ChangeUrl()
    {
        const int blue = (int)Color.Blue;

        var cut = _testContext.RenderComponent<Collection>();
        var nav = _testContext.Services.GetRequiredService<FakeNavigationManager>();

        var blueButton = cut.Find(".ms-u");

        var invariant = CultureInfo.InvariantCulture;

        blueButton.Click();

        Assert.Contains(blue.ToString(invariant), nav.Uri);
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
