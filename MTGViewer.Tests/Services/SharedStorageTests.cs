using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;
using Moq;

using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Services
{
    public class SharedStorageTests : IAsyncLifetime
    {
        private readonly ServiceProvider _services;
        private readonly CardDbContext _dbContext;
        private readonly ExpandableSharedService _sharedStorage;


        public SharedStorageTests()
        {
            _services = TestFactory.ServiceProvider();
            _dbContext = TestFactory.CardDbContext(_services);

            _sharedStorage = new(Mock.Of<IConfiguration>(), _dbContext);
        }


        public async Task InitializeAsync()
        {
            await _dbContext.SeedAsync();
        }


        public async Task DisposeAsync()
        {
            await _services.DisposeAsync();
            await _dbContext.DisposeAsync();
            _sharedStorage.Dispose();
        }


        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task Return_ValidCard_Success(int cardIndex)
        {
            var copies = 4;
            var card = await _dbContext.Cards
                .AsNoTracking()
                .Skip(cardIndex)
                .FirstAsync();

            var sharedQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == card.Id)
                .Include(ca => ca.Location)
                .AsNoTracking();

            var sharedBefore = await sharedQuery.FirstOrDefaultAsync();
            var beforeAmount = sharedBefore?.Amount ?? 0;

            await _sharedStorage.ReturnAsync(card, copies);

            var sharedAfter = await sharedQuery.FirstAsync();

            Assert.IsType<Box>(sharedAfter.Location);

            Assert.Equal(card.Id, sharedAfter.CardId);
            Assert.Equal(copies, sharedAfter.Amount - beforeAmount);
        }


        [Fact]
        public async Task Return_NullCard_ThrowException()
        {
            var copies = 4;
            Card card = null;

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _sharedStorage.ReturnAsync(card, copies) );
        }


        [Theory]
        [InlineData(-3)]
        [InlineData(0)]
        [InlineData(-10)]
        public async Task Return_InvalidCopies_ThrowsException(int copies)
        {
            var card = await _dbContext.Cards
                .AsNoTracking()
                .FirstAsync();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _sharedStorage.ReturnAsync(card, copies) );
        }


        [Fact]
        public async Task Return_EmptyReturns_NoChange()
        {
            var sharedAmountQuery = _dbContext.Amounts
                .Select(ba => ba.Amount);

            var sharedBefore = await sharedAmountQuery.SumAsync();
            await _sharedStorage.ReturnAsync();
            var sharedAfter = await sharedAmountQuery.SumAsync();

            Assert.Equal(sharedBefore, sharedAfter);
        }


        [Fact]
        public async Task Return_NoBoxes_ThrowsException()
        {
            var boxes = await _dbContext.Boxes
                .Include(b => b.Cards)
                .ToListAsync();

            _dbContext.Amounts.RemoveRange(boxes.SelectMany(b => b.Cards));
            _dbContext.Boxes.RemoveRange(boxes);

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            var copies = 4;
            var card = await _dbContext.Cards
                .AsNoTracking()
                .FirstAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _sharedStorage.ReturnAsync(card, copies) );
        }


        [Fact]
        public async Task Return_NewCard_Success()
        {
            var copies = 4;
            var card = await _dbContext.Cards
                .FirstAsync();

            var boxAmountQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == card.Id)
                .Include(ca => ca.Location);

            var boxAmounts = await boxAmountQuery.ToListAsync();
            _dbContext.Amounts.RemoveRange(boxAmounts);

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            var sharedQuery = boxAmountQuery.AsNoTracking();

            var sharedBefore = await sharedQuery.FirstOrDefaultAsync();
            var beforeAmount = sharedBefore?.Amount ?? 0;

            await _sharedStorage.ReturnAsync(card, copies);

            var sharedAfter = await sharedQuery.FirstAsync();

            Assert.IsType<Box>(sharedAfter.Location);

            Assert.Equal(card.Id, sharedAfter.CardId);
            Assert.Equal(copies, sharedAfter.Amount - beforeAmount);
        }


        [Fact]
        public async Task Optimize_SmallAmount_NoChange()
        {
            var copies = 6;
            var card = await _dbContext.Cards.AsNoTracking().FirstAsync();

            var sharedAmountQuery = _dbContext.Amounts
                .Select(ca => ca.Amount);

            await _sharedStorage.ReturnAsync(card, copies);
            var sharedBefore = await sharedAmountQuery.SumAsync();

            await _sharedStorage.OptimizeAsync();
            var sharedAfter = await sharedAmountQuery.SumAsync();

            Assert.Equal(sharedBefore, sharedAfter);
        }


        [Fact]
        public async Task Optimize_LargeAmount_SplitMultiple()
        {
            var copies = 120;
            var card = await _dbContext.Cards.AsNoTracking().FirstAsync();

            var spotQuery = _dbContext.Amounts
                .Where(ca => ca.CardId == card.Id)
                .Include(ca => ca.Location)
                .AsNoTracking();

            var sharedAmountQuery = _dbContext.Amounts
                .Select(ca => ca.Amount);

            await _sharedStorage.ReturnAsync(card, copies);
            var oldSpots = await spotQuery.ToListAsync();
            var sharedBefore = await sharedAmountQuery.SumAsync();

            await _sharedStorage.OptimizeAsync();
            var newSpots = await spotQuery.ToListAsync();
            var sharedAfter = await sharedAmountQuery.SumAsync();

            Assert.All(newSpots, ca => Assert.IsType<Box>(ca.Location));
            Assert.All(newSpots, ca => Assert.True(ca.Amount < copies));

            Assert.Equal(sharedBefore, sharedAfter);
            Assert.Equal(
                oldSpots.Sum(ca => ca.Amount),
                newSpots.Sum(ca => ca.Amount));
        }
    }
}