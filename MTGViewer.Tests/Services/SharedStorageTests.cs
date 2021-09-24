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

            var boxesQuery = _dbContext.Amounts
                .Where(ca => ca.Location is Box && ca.CardId == card.Id)
                .Include(ca => ca.Location)
                .AsNoTracking();

            var boxesBefore = await boxesQuery.ToListAsync();
            await _sharedStorage.ReturnAsync(card, copies);

            var boxesAfter = await boxesQuery.ToListAsync();
            var boxesChange = boxesAfter.Sum(ca => ca.Amount)
                - boxesBefore.Sum(ca => ca.Amount);

            Assert.All(boxesAfter, ca => Assert.IsType<Box>(ca.Location));
            Assert.All(boxesAfter, ca => Assert.Equal(card.Id, ca.CardId));

            Assert.Equal(copies, boxesChange);
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
                .Where(ca => ca.Location is Box)
                .Select(ca => ca.Amount);

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

            var boxesTrackedQuery = _dbContext.Amounts
                .Where(ca => ca.Location is Box && ca.CardId == card.Id)
                .Include(ca => ca.Location);

            var boxAmounts = await boxesTrackedQuery.ToListAsync();
            _dbContext.Amounts.RemoveRange(boxAmounts);

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            var boxesQuery = boxesTrackedQuery.AsNoTracking();

            var boxesBefore = await boxesQuery.ToListAsync();
            await _sharedStorage.ReturnAsync(card, copies);

            var boxesAfter = await boxesQuery.ToListAsync();
            var boxesChange = boxesAfter.Sum(ca => ca.Amount)
                - boxesBefore.Sum(ca => ca.Amount);

            Assert.All(boxesAfter, ca => Assert.IsType<Box>(ca.Location));
            Assert.All(boxesAfter, ca => Assert.Equal(card.Id, ca.CardId));

            Assert.Equal(copies, boxesChange);
        }


        [Fact]
        public async Task Optimize_SmallAmount_NoChange()
        {
            var copies = 6;
            var card = await _dbContext.Cards.AsNoTracking().FirstAsync();

            var boxAmountQuery = _dbContext.Amounts
                .Where(ca => ca.Location is Box)
                .Select(ca => ca.Amount);

            await _sharedStorage.ReturnAsync(card, copies);
            var boxesBefore = await boxAmountQuery.SumAsync();

            await _sharedStorage.OptimizeAsync();
            var boxesAfter = await boxAmountQuery.SumAsync();

            Assert.Equal(boxesBefore, boxesAfter);
        }


        [Fact]
        public async Task Optimize_LargeAmount_SplitMultiple()
        {
            var copies = 120;
            var card = await _dbContext.Cards.AsNoTracking().FirstAsync();

            var boxesQuery = _dbContext.Amounts
                .Where(ca => ca.Location is Box && ca.CardId == card.Id)
                .Include(ca => ca.Location)
                .AsNoTracking();

            await _sharedStorage.ReturnAsync(card, copies);
            var oldSpots = await boxesQuery.ToListAsync();
            var totalBefore = oldSpots.Sum(ca => ca.Amount);

            await _sharedStorage.OptimizeAsync();
            var newSpots = await boxesQuery.ToListAsync();
            var totalAfter = newSpots.Sum(ca => ca.Amount);

            Assert.All(newSpots, ca => Assert.IsType<Box>(ca.Location));
            Assert.All(newSpots, ca => Assert.True(ca.Amount < copies));

            Assert.Equal(totalBefore, totalAfter);
        }
    }
}