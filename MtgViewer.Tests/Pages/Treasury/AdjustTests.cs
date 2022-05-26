using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Bunit;
using Bunit.TestDoubles;
using Xunit;

using MtgViewer.Data.Infrastructure;
using MtgViewer.Data;
using MtgViewer.Pages.Treasury;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Pages.Treasury;

public sealed class AdjustTests : IAsyncLifetime, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;
    private readonly TestContext _testContext;

    public AdjustTests(
        IServiceProvider services,
        CardDbContext dbContext,
        TestDataGenerator testGen)
    {
        _services = services;
        _dbContext = dbContext;

        _testGen = testGen;
        _testContext = new TestContext();
    }

    public async Task InitializeAsync()
    {
        _testContext.AddFakePersistentComponentState();

        _testContext.Services.AddFallbackServiceProvider(_services);

        await _testGen.SeedAsync();
    }

    public async Task DisposeAsync()
    {
        _testContext.Dispose();

        await _testGen.ClearAsync();
    }

    void IDisposable.Dispose() => _testContext.Dispose();

    private static void ChangeInput<T>(IRenderedComponent<Adjust> cut, string cssSelector, T newValue)
        => cut.Find(cssSelector).Change(newValue);

    [Fact]
    public async Task LoadData_InvalidBox_KeepsLoading()
    {
        int invalidBox = await _dbContext.Decks
            .Select(d => d.Id)
            .FirstAsync();

        var cut = _testContext.RenderComponent<Adjust>(p => p
            .Add(a => a.BoxId, invalidBox));

        var title = cut.Find("h1");

        Assert.Equal("Treasury Adjustment", title.TextContent.Trim());
    }

    [Fact]
    public void LoadData_NewBox_AddBox()
    {
        var cut = _testContext.RenderComponent<Adjust>();

        var title = cut.Find("h1");

        Assert.Equal("Add New Box", title.TextContent.Trim());
    }

    [Fact]
    public async Task LoadData_ExistingBox_EditBox()
    {
        var box = await _dbContext.Boxes.FirstAsync();

        var cut = _testContext.RenderComponent<Adjust>(p => p
            .Add(a => a.BoxId, box.Id));

        var title = cut.Find("h1");

        Assert.Equal($"Edit {box.Name}", title.TextContent.Trim());
    }

    [Fact]
    public async Task SaveBox_NewBox_CreateBox()
    {
        const int addCapacity = 25;

        var cut = _testContext.RenderComponent<Adjust>();

        ChangeInput(cut, $"input#{BoxDto.PropertyId(b => b.Name)}", "New Box Name");
        ChangeInput(cut, $"input#{BinDto.PropertyId(b => b.Name)}", "New Bin Name");
        ChangeInput(cut, $"input#{BoxDto.PropertyId(b => b.Capacity)}", addCapacity);

        int beforeBoxes = await _dbContext.Boxes.CountAsync();

        var form = cut.FindComponent<EditForm>();

        await cut.InvokeAsync(() => form.Instance.OnValidSubmit.InvokeAsync());

        int afterBoxes = await _dbContext.Boxes.CountAsync();

        Assert.Equal(1, afterBoxes - beforeBoxes);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(5)]
    public async Task SaveBox_ChangeCapacity_ModifyBox(int modCapacity)
    {
        var box = await _dbContext.Boxes.FirstAsync();

        int newCapacity = box.Capacity + modCapacity;

        var cut = _testContext.RenderComponent<Adjust>(p => p
            .Add(b => b.BoxId, box.Id));

        ChangeInput(cut, $"input#{BoxDto.PropertyId(b => b.Capacity)}", newCapacity);

        var form = cut.FindComponent<EditForm>();

        await cut.InvokeAsync(() => form.Instance.OnValidSubmit.InvokeAsync());

        int updatedCapacity = await _dbContext.Boxes
            .Where(b => b.Id == box.Id)
            .Select(b => b.Capacity)
            .FirstOrDefaultAsync();

        Assert.Equal(newCapacity, updatedCapacity);
    }

    [Fact]
    public async Task SaveBox_ChangeName_ModifyBox()
    {
        const string newName = "New Box Name";

        var box = await _dbContext.Boxes.FirstAsync();

        var cut = _testContext.RenderComponent<Adjust>(p => p
            .Add(b => b.BoxId, box.Id));

        ChangeInput(cut, $"input#{BoxDto.PropertyId(b => b.Name)}", newName);

        var form = cut.FindComponent<EditForm>();

        await cut.InvokeAsync(() => form.Instance.OnValidSubmit.InvokeAsync());

        string? updatedName = await _dbContext.Boxes
            .Where(b => b.Id == box.Id)
            .Select(b => b.Name)
            .FirstOrDefaultAsync();

        Assert.Equal(newName, updatedName);
    }

    [Fact]
    public async Task SaveBox_ChangeBinName_ModifyBox()
    {
        const string newName = "New Bin Name";

        var box = await _dbContext.Boxes.FirstAsync();

        var cut = _testContext.RenderComponent<Adjust>(p => p
            .Add(b => b.BoxId, box.Id));

        ChangeInput(cut, $"input#{BinDto.PropertyId(b => b.Name)}", newName);

        var form = cut.FindComponent<EditForm>();

        await cut.InvokeAsync(() => form.Instance.OnValidSubmit.InvokeAsync());

        string? updatedName = await _dbContext.Boxes
            .Where(b => b.Id == box.Id)
            .Select(b => b.Bin.Name)
            .FirstOrDefaultAsync();

        Assert.Equal(newName, updatedName);
    }
}
