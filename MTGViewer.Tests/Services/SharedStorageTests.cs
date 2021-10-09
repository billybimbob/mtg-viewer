using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Tests.Utils;

#nullable enable

namespace MTGViewer.Tests.Services
{
    public class SharedStorageTests : IAsyncLifetime
    {
        private readonly CardDbContext _dbContext;
        private readonly ISharedStorage _sharedStorage;
        private readonly TestDataGenerator _testGen;


        public SharedStorageTests(
            CardDbContext dbContext, 
            ISharedStorage sharedStorage, 
            TestDataGenerator testGen)
        {
            _dbContext = dbContext;
            _sharedStorage = sharedStorage;
            _testGen = testGen;
        }


        public Task InitializeAsync() => _testGen.SeedAsync();

        public Task DisposeAsync() => _testGen.ClearAsync();


        public IQueryable<Card> Cards =>
            _dbContext.Cards.AsNoTracking();

        public IQueryable<CardAmount> BoxAmounts => 
            _dbContext.Amounts
                .Where(ca => ca.Location is Box)
                .Include(ca => ca.Location)
                .AsNoTracking();


        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task Return_ValidCard_Success(int cardIndex)
        {
            var copies = 4;
            var card = await Cards.Skip(cardIndex).FirstAsync();

            var cardBoxes = BoxAmounts.Where(ca => ca.CardId == card.Id);

            var boxesBefore = await cardBoxes.ToListAsync();
            await _sharedStorage.ReturnAsync(card, copies);
            var boxesAfter = await cardBoxes.ToListAsync();

            var boxesAfterIds = boxesAfter.Select(ca => ca.CardId);
            var boxesChange = boxesAfter.Sum(ca => ca.Amount) - boxesBefore.Sum(ca => ca.Amount);

            Assert.All(boxesAfter, ca => Assert.IsType<Box>(ca.Location));

            Assert.Contains(card.Id, boxesAfterIds);
            Assert.Equal(copies, boxesChange);
        }


        [Fact]
        public async Task Return_NullCard_ThrowException()
        {
            var copies = 4;
            Card? card = null;

            Task SharedReturn() => _sharedStorage.ReturnAsync(card!, copies);

            await Assert.ThrowsAsync<ArgumentException>(SharedReturn);
        }


        [Theory]
        [InlineData(-3)]
        [InlineData(0)]
        [InlineData(-10)]
        public async Task Return_InvalidCopies_ThrowsException(int copies)
        {
            var card = await _dbContext.Cards.FirstAsync();

            Task SharedReturn() => _sharedStorage.ReturnAsync(card, copies);

            await Assert.ThrowsAsync<ArgumentException>(SharedReturn);
        }


        [Fact]
        public async Task Return_EmptyReturns_ThrowsException()
        {
            var emptyReturns = Enumerable.Empty<CardReturn>();

            Task SharedReturn() => _sharedStorage.ReturnAsync(emptyReturns);

            await Assert.ThrowsAsync<ArgumentException>(SharedReturn);
        }


        [Theory]
        [InlineData(1, 2, 0.5f)]
        [InlineData(2, 3, 0.25f)]
        public async Task Return_MultipleCards_Success(int cardIdx1, int cardIdx2, float split)
        {
            var copies = 12;

            var card1 = await _dbContext.Cards.Skip(cardIdx1).FirstAsync();
            var card2 = await _dbContext.Cards.Skip(cardIdx2).FirstAsync();

            int copy1 = (int)(copies * split);
            int copy2 = (int)(copies * (1 - split));

            copies = copy1 + copy2;

            var cardBoxes = BoxAmounts.Where(ca => 
                ca.CardId == card1.Id || ca.CardId == card2.Id);

            var boxesBefore = await cardBoxes.ToListAsync();

            await _sharedStorage.ReturnAsync(
                new CardReturn(card1, copy1), new CardReturn(card2, copy2));

            var boxesAfter = await cardBoxes.ToListAsync();
            var boxesAfterIds = boxesAfter.Select(ca => ca.CardId);

            var box1BeforeAmount = boxesBefore
                .Where(ca => ca.CardId == card1.Id)
                .Sum(ca => ca.Amount);

            var box1AfterAmount = boxesAfter
                .Where(ca => ca.CardId == card1.Id)
                .Sum(ca => ca.Amount);

            var box2BeforeAmount = boxesBefore
                .Where(ca => ca.CardId == card2.Id)
                .Sum(ca => ca.Amount);

            var box2AfterAmount = boxesAfter
                .Where(ca => ca.CardId == card2.Id)
                .Sum(ca => ca.Amount);

            var boxesChange = boxesAfter.Sum(ca => ca.Amount) - boxesBefore.Sum(ca => ca.Amount);
            var box1Change = box1AfterAmount - box1BeforeAmount;
            var box2Change = box2AfterAmount - box2BeforeAmount;

            Assert.All(boxesAfter, ca => Assert.IsType<Box>(ca.Location));

            Assert.Contains(card1.Id, boxesAfterIds);
            Assert.Contains(card2.Id, boxesAfterIds);

            Assert.Equal(copies, boxesChange);
            Assert.Equal(copy1, box1Change);
            Assert.Equal(copy2, box2Change);
        }


        [Fact]
        public async Task Return_NoBoxes_ThrowsException()
        {
            var boxesTracked = await _dbContext.Boxes
                .Include(b => b.Cards)
                .AsTracking()
                .ToListAsync();

            _dbContext.Amounts.RemoveRange(boxesTracked.SelectMany(b => b.Cards));
            _dbContext.Boxes.RemoveRange(boxesTracked);

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            var copies = 4;
            var card = await Cards.FirstAsync();

            Task SharedReturn() => _sharedStorage.ReturnAsync(card, copies);

            await Assert.ThrowsAsync<InvalidOperationException>(SharedReturn);
        }


        [Fact]
        public async Task Return_NewCard_Success()
        {
            var copies = 4;
            var card = await _dbContext.Cards.FirstAsync();

            var cardBoxes = BoxAmounts.Where(ca => ca.CardId == card.Id);
            var cardBoxesTracked = cardBoxes.AsTracking();

            var boxAmounts = await cardBoxesTracked.ToListAsync();
            _dbContext.Amounts.RemoveRange(boxAmounts);

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            var boxesBefore = await cardBoxes.ToListAsync();
            var transaction = await _sharedStorage.ReturnAsync(card, copies);

            var boxesAfter = await cardBoxes.ToListAsync();
            var boxesChange = boxesAfter.Sum(ca => ca.Amount) - boxesBefore.Sum(ca => ca.Amount);

            Assert.NotNull(transaction);

            Assert.All(boxesAfter, ca => Assert.IsType<Box>(ca.Location));
            Assert.All(boxesAfter, ca => Assert.Equal(card.Id, ca.CardId));

            Assert.Equal(copies, boxesChange);
        }


        [Fact]
        public async Task Optimize_SmallAmount_NoChange()
        {
            var copies = 6;
            var card = await Cards.FirstAsync();

            var boxAmounts = BoxAmounts.Select(ca => ca.Amount);

            await _sharedStorage.ReturnAsync(card, copies);

            var boxesBefore = await boxAmounts.SumAsync();
            var transaction = await _sharedStorage.OptimizeAsync();
            var boxesAfter = await boxAmounts.SumAsync();

            Assert.Null(transaction);
            Assert.Equal(boxesBefore, boxesAfter);
        }


        [Fact]
        public async Task Optimize_LargeAmount_SplitMultiple()
        {
            var copies = 120;
            var card = await Cards.FirstAsync();

            var cardBoxes = BoxAmounts.Where(ca => ca.CardId == card.Id);

            await _sharedStorage.ReturnAsync(card, copies);

            var oldSpots = await cardBoxes.ToListAsync();
            var totalBefore = oldSpots.Sum(ca => ca.Amount);

            var transaction = await _sharedStorage.OptimizeAsync();

            var newSpots = await cardBoxes.ToListAsync();
            var totalAfter = newSpots.Sum(ca => ca.Amount);

            Assert.NotNull(transaction);

            Assert.All(newSpots, ca => Assert.IsType<Box>(ca.Location));
            Assert.All(newSpots, ca => Assert.True(ca.Amount < copies));

            Assert.Equal(totalBefore, totalAfter);
        }
    }
}