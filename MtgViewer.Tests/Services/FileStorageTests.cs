using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MtgViewer.Data;
using MtgViewer.Services;
using MtgViewer.Services.Infrastructure;
using MtgViewer.Services.Seed;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Services;

[TestCaseOrderer("MtgViewer.Tests.Utils." + nameof(PriorityOrderer), "MtgViewer.Tests")]
public class FileStorageTests : IClassFixture<TempFileName>, IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly CardDataGenerator _cardGen;
    private readonly TestDataGenerator _testGen;

    private readonly SeedHandler _seedHandler;
    private readonly FileCardStorage _fileStorage;
    private readonly string _tempFileName;

    public FileStorageTests(
        CardDbContext dbContext,
        CardDataGenerator cardGen,
        TestDataGenerator testGen,
        SeedHandler seedHandler,
        FileCardStorage fileStorage,
        TempFileName tempFile)
    {
        _dbContext = dbContext;
        _cardGen = cardGen;
        _testGen = testGen;
        _seedHandler = seedHandler;
        _fileStorage = fileStorage;
        _tempFileName = tempFile.Value;
    }

    public Task InitializeAsync() => _testGen.SetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [TestPriority(1)]
    public async Task Write_Temp_Success()
    {
        bool anyBefore = await _dbContext.Cards.AnyAsync();

        await _cardGen.GenerateAsync();
        await _seedHandler.WriteBackupAsync(_tempFileName);

        var tempInfo = new FileInfo(_tempFileName);
        bool anyAfter = await _dbContext.Cards.AnyAsync();

        Assert.False(anyBefore);

        Assert.True(File.Exists(_tempFileName));
        Assert.True(tempInfo.Length > 0);

        Assert.True(anyAfter);
    }

    [Fact]
    [TestPriority(2)]
    public async Task Seed_Temp_Success()
    {
        bool anyBefore = await _dbContext.Cards.AnyAsync();

        await _seedHandler.SeedAsync(_tempFileName);

        bool anyAfter = await _dbContext.Cards.AnyAsync();

        Assert.False(anyBefore, "Card Db should be empty");
        Assert.True(anyAfter, "Card Db should be filled");
    }

    [Fact]
    [TestPriority(2)]
    public async Task Add_Temp_Success()
    {
        const string fileName = "tempFile.json";

        bool anyBefore = await _dbContext.Cards.AnyAsync();
        var tempInfo = new FileInfo(_tempFileName);

        await using var tempStream = File.OpenRead(_tempFileName);

        var formFile = new FormFile(tempStream, 0L, tempInfo.Length, fileName, fileName);
        await using var fileStream = formFile.OpenReadStream();

        await _fileStorage.AddFromJsonAsync(fileStream);

        bool anyAfter = await _dbContext.Cards.AnyAsync();

        Assert.False(anyBefore, "Card Db should be empty");
        Assert.True(anyAfter, "Card Db should be filled");
    }
}
