using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Services;

[TestCaseOrderer("MTGViewer.Tests.Utils." + nameof(PriorityOrderer), "MTGViewer.Tests")]
public class FileStorageTests : IClassFixture<TempFileName>, IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly CardDataGenerator _cardGen;
    private readonly TestDataGenerator _testGen;
    private readonly FileCardStorage _fileStorage;
    private readonly string _tempFileName;

    public FileStorageTests(
        CardDbContext dbContext,
        CardDataGenerator cardGen,
        TestDataGenerator testGen,
        FileCardStorage fileStorage,
        TempFileName tempFile)
    {
        _dbContext = dbContext;
        _cardGen = cardGen;
        _testGen = testGen;
        _fileStorage = fileStorage;
        _tempFileName = tempFile.Value;
    }

    public Task InitializeAsync() => _testGen.SetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [TestPriority(1)]
    public async Task Write_Temp_Success()
    {
        var anyBefore = await _dbContext.Cards.AnyAsync();

        await _cardGen.GenerateAsync();
        await _fileStorage.WriteBackupAsync(_tempFileName);

        var tempInfo = new FileInfo(_tempFileName);
        var anyAfter = await _dbContext.Cards.AnyAsync();

        Assert.False(anyBefore);

        Assert.True(File.Exists(_tempFileName));
        Assert.True(tempInfo.Length > 0);

        Assert.True(anyAfter);
    }

    [Fact]
    [TestPriority(2)]
    public async Task Seed_Temp_Success()
    {
        var anyBefore = await _dbContext.Cards.AnyAsync();

        await _fileStorage.JsonSeedAsync(_tempFileName);

        var anyAfter = await _dbContext.Cards.AnyAsync();

        Assert.False(anyBefore, "Card Db should be empty");
        Assert.True(anyAfter, "Card Db should be filled");
    }

    [Fact]
    [TestPriority(2)]
    public async Task Add_Temp_Success()
    {
        const string fileName = "tempFile.json";

        var anyBefore = await _dbContext.Cards.AnyAsync();
        var tempInfo = new FileInfo(_tempFileName);

        await using var tempStream = File.OpenRead(_tempFileName);

        var formFile = new FormFile(tempStream, 0L, tempInfo.Length, fileName, fileName);
        await using var fileStream = formFile.OpenReadStream();

        await _fileStorage.JsonAddAsync(fileStream);

        var anyAfter = await _dbContext.Cards.AnyAsync();

        Assert.False(anyBefore, "Card Db should be empty");
        Assert.True(anyAfter, "Card Db should be filled");
    }
}
