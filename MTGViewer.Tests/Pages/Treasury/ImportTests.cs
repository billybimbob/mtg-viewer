using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components.Forms;
using Bunit;
using Moq;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Treasury;
using MTGViewer.Services.Internal;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Treasury;

public sealed class ImportTests : IAsyncLifetime, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly TestDataGenerator _testGen;
    private readonly TestContext _testContext;

    public ImportTests(IServiceProvider services, TestDataGenerator testGen)
    {
        _services = services;
        _testGen = testGen;
        _testContext = new TestContext();
    }

    public async Task InitializeAsync()
    {
        _testContext.JSInterop
            .SetupVoid(invoke => invoke.Identifier == "Blazor._internal.InputFile.init")
            .SetVoidResult();

        _testContext.Services.AddFallbackServiceProvider(_services);

        await _testGen.SeedAsync();
    }

    public async Task DisposeAsync()
    {
        _testContext.Dispose();

        await _testGen.ClearAsync();
    }

    void IDisposable.Dispose() => _testContext.Dispose();

    private static InputFileChangeEventArgs GetFileInput(
        CardData data,
        string? fileName = null,
        long? length = null,
        string? contentType = null)
    {
        var options = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve
        };

        byte[] jsonEncode = JsonSerializer.SerializeToUtf8Bytes(data);

        // stream is fine to not dispose
        var stream = new MemoryStream(jsonEncode);

        var browserFile = new Mock<IBrowserFile>();

        browserFile
            .SetupGet(b => b.Name)
            .Returns(fileName ?? "file.json");

        browserFile
            .SetupGet(b => b.Size)
            .Returns(length ?? jsonEncode.LongLength);

        browserFile
            .SetupGet(b => b.ContentType)
            .Returns(contentType ?? "application/json");

        browserFile
            .Setup(b => b.OpenReadStream(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Returns(stream);

        return new InputFileChangeEventArgs(new[] { browserFile.Object });
    }

    [Fact]
    public async Task ChooseFile_LargeLength_InvalidInput()
    {
        var cut = _testContext.RenderComponent<Import>();
        var fileInput = cut.FindComponent<InputFile>();

        const long largeLength = 1_000_000_000_000L;

        var emptyData = new CardData();
        var file = GetFileInput(emptyData, length: largeLength);

        await cut.InvokeAsync(() => fileInput.Instance.OnChange.InvokeAsync(file));

        var errors = cut.FindAll("span.text-danger");

        Assert.Equal(1, errors.Count);
    }

    [Fact]
    public async Task ChooseFile_InvalidContentType_InvalidInput()
    {
        var cut = _testContext.RenderComponent<Import>();
        var fileInput = cut.FindComponent<InputFile>();

        var emptyData = new CardData();
        var file = GetFileInput(emptyData, contentType: "application/pdf");

        await cut.InvokeAsync(() => fileInput.Instance.OnChange.InvokeAsync(file));

        var errors = cut.FindAll("span.text-danger");

        Assert.Equal(1, errors.Count);
    }

    [Fact]
    public async Task ChooseFile_InvalidFileName_InvalidInput()
    {
        var cut = _testContext.RenderComponent<Import>();
        var fileInput = cut.FindComponent<InputFile>();

        var emptyData = new CardData();
        var file = GetFileInput(emptyData, fileName: "file.txt");

        await cut.InvokeAsync(() => fileInput.Instance.OnChange.InvokeAsync(file));

        var errors = cut.FindAll("span.text-danger");

        Assert.Equal(1, errors.Count);
    }

    [Fact]
    public async Task ChooseFile_ValidFile_CanUpload()
    {
        var cut = _testContext.RenderComponent<Import>();
        var fileInput = cut.FindComponent<InputFile>();

        var validData = new CardData
        {
            Cards = new[] { new Card() }
        };

        var file = GetFileInput(validData);

        var beforeSubmit = cut.FindAll("button[type=\"submit\"][disabled]");

        await cut.InvokeAsync(() => fileInput.Instance.OnChange.InvokeAsync(file));

        var afterSubmit = cut.FindAll("button[type=\"submit\"]:not([disabled])");

        Assert.Equal(1, beforeSubmit.Count);
        Assert.Equal(1, afterSubmit.Count);
    }

    // TODO: add file update test cases
}
