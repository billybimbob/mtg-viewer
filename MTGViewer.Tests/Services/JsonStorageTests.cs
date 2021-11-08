using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Services
{
    [TestCaseOrderer("MTGViewer.Tests.Utils." + nameof(PriorityOrderer), "MTGViewer.Tests")]
    public class JsonStorageTests : IClassFixture<TempFileName>, IAsyncLifetime
    {
        private readonly CardDbContext _dbContext;
        private readonly CardDataGenerator _cardGen;
        private readonly TestDataGenerator _testGen;
        private readonly JsonCardStorage _jsonStorage;
        private readonly string _tempFileName;

        public JsonStorageTests(
            CardDbContext dbContext,
            CardDataGenerator cardGen,
            TestDataGenerator testGen,
            JsonCardStorage jsonStorage, 
            TempFileName tempFile)
        {
            _dbContext = dbContext;
            _cardGen = cardGen;
            _testGen = testGen;
            _jsonStorage = jsonStorage;
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
            await _jsonStorage.WriteToJsonAsync(new() { Path = _tempFileName });

            var tempInfo = new FileInfo(_tempFileName);
            var anyAfter = await _dbContext.Cards.AnyAsync();

            Assert.False(anyBefore);

            Assert.True(File.Exists(_tempFileName));
            Assert.True(tempInfo.Length > 0);

            Assert.True(anyAfter);
        }


        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [TestPriority(2)]
        public async Task Add_Temp_Success(bool seeding)
        {
            var anyBefore = await _dbContext.Cards.AnyAsync();

            var success = await _jsonStorage.AddFromJsonAsync(
                new()
                {
                    Path = _tempFileName,
                    Seeding = seeding
                });

            var anyAfter = await _dbContext.Cards.AnyAsync();

            Assert.False(anyBefore, "Card Db should be empty");
            Assert.True(success, "Json Add Failed");
            Assert.True(anyAfter, "Card Db should be filled");
        }
    }
}