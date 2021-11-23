using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Services;


public class TreasuryTests : IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly ITreasury _treasury;
    private readonly TestDataGenerator _testGen;


    public TreasuryTests(
        CardDbContext dbContext, 
        ITreasury treasury, 
        TestDataGenerator testGen)
    {
        _dbContext = dbContext;
        _treasury = treasury;
        _testGen = testGen;
    }


    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();


    public IQueryable<Card> AllCards =>
        _dbContext.Cards.AsNoTracking();

    public IQueryable<Amount> BoxAmounts => 
        _treasury.Cards
            .Include(ca => ca.Location)
            .AsNoTracking();


    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Return_ValidCard_Success(int cardIndex)
    {
        var copies = 4;
        var card = await AllCards.Skip(cardIndex).FirstAsync();

        var cardBoxes = BoxAmounts.Where(ca => ca.CardId == card.Id);

        var boxesBefore = await cardBoxes.Select(ca => ca.NumCopies).SumAsync();
        await _treasury.ReturnAsync(card, copies);
        var boxesAfter = await cardBoxes.ToListAsync();

        var boxesAfterIds = boxesAfter.Select(ca => ca.CardId);
        var boxesChange = boxesAfter.Sum(ca => ca.NumCopies) - boxesBefore;

        Assert.All(boxesAfter, ca =>
            Assert.IsType<Box>(ca.Location));

        Assert.Contains(card.Id, boxesAfterIds);
        Assert.Equal(copies, boxesChange);
    }


    [Fact]
    public async Task Return_NullCard_ReturnsNull()
    {
        var copies = 4;
        Card? card = null;

        var transaction = await _treasury.ReturnAsync(card!, copies);

        Assert.Null(transaction);
    }


    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Return_InvalidCopies_ReturnsNull(int copies)
    {
        var card = await _dbContext.Cards.FirstAsync();

        var transaction = await _treasury.ReturnAsync(card, copies);

        Assert.Null(transaction);
    }


    [Fact]
    public async Task Return_EmptyReturns_ReturnsNull()
    {
        var emptyReturns = Enumerable.Empty<CardReturn>();

        var transaction = await _treasury.ReturnAsync(emptyReturns);

        Assert.Null(transaction);
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

        var returns = new [] { new CardReturn(card1, copy1), new CardReturn(card2, copy2) };

        await _treasury.ReturnAsync(returns);

        var boxesAfter = await cardBoxes.ToListAsync();
        var boxesAfterIds = boxesAfter.Select(ca => ca.CardId);

        var box1BeforeAmount = boxesBefore
            .Where(ca => ca.CardId == card1.Id)
            .Sum(ca => ca.NumCopies);

        var box1AfterAmount = boxesAfter
            .Where(ca => ca.CardId == card1.Id)
            .Sum(ca => ca.NumCopies);

        var box2BeforeAmount = boxesBefore
            .Where(ca => ca.CardId == card2.Id)
            .Sum(ca => ca.NumCopies);

        var box2AfterAmount = boxesAfter
            .Where(ca => ca.CardId == card2.Id)
            .Sum(ca => ca.NumCopies);

        var boxesChange = boxesAfter.Sum(ca => ca.NumCopies) - boxesBefore.Sum(ca => ca.NumCopies);
        var box1Change = box1AfterAmount - box1BeforeAmount;
        var box2Change = box2AfterAmount - box2BeforeAmount;

        Assert.All(boxesAfter, ca =>
            Assert.IsType<Box>(ca.Location));

        Assert.Contains(card1.Id, boxesAfterIds);
        Assert.Contains(card2.Id, boxesAfterIds);

        Assert.Equal(copies, boxesChange);
        Assert.Equal(copy1, box1Change);
        Assert.Equal(copy2, box2Change);
    }


    [Fact]
    public async Task Return_NoBoxes_ReturnsNull()
    {
        var boxesTracked = await _dbContext.Boxes
            .Include(b => b.Cards)
            .ToListAsync();

        _dbContext.Amounts.RemoveRange(boxesTracked.SelectMany(b => b.Cards));
        _dbContext.Boxes.RemoveRange(boxesTracked);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var copies = 4;
        var card = await AllCards.FirstAsync();

        var transaction = await _treasury.ReturnAsync(card, copies);

        Assert.Null(transaction);
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

        var boxesBefore = await cardBoxes.Select(a => a.NumCopies).SumAsync();
        var transaction = await _treasury.ReturnAsync(card, copies);

        var boxesAfter = await cardBoxes.ToListAsync();
        var boxesChange = boxesAfter.Sum(ca => ca.NumCopies) - boxesBefore;

        Assert.NotNull(transaction);
        Assert.Equal(copies, boxesChange);

        Assert.All(boxesAfter, ca =>
            Assert.IsType<Box>(ca.Location));

        Assert.All(boxesAfter, ca =>
            Assert.Equal(card.Id, ca.CardId));
    }


    [Fact]
    public async Task Optimize_SmallAmount_NoChange()
    {
        var copies = 6;
        var card = await AllCards.FirstAsync();

        var boxAmounts = BoxAmounts.Select(ca => ca.NumCopies);

        await _treasury.ReturnAsync(card, copies);

        var boxesBefore = await boxAmounts.SumAsync();
        var transaction = await _treasury.OptimizeAsync();
        var boxesAfter = await boxAmounts.SumAsync();

        Assert.Null(transaction);
        Assert.Equal(boxesBefore, boxesAfter);
    }


    [Fact]
    public async Task Optimize_LargeAmount_SplitMultiple()
    {
        var copies = 120;
        var card = await AllCards.FirstAsync();

        var cardBoxes = BoxAmounts.Where(ca => ca.CardId == card.Id);

        await _treasury.ReturnAsync(card, copies);

        var oldSpots = await cardBoxes.ToListAsync();
        var totalBefore = oldSpots.Sum(ca => ca.NumCopies);

        var transaction = await _treasury.OptimizeAsync();

        var newSpots = await cardBoxes.ToListAsync();
        var totalAfter = newSpots.Sum(ca => ca.NumCopies);

        Assert.NotNull(transaction);
        Assert.Equal(totalBefore, totalAfter);

        Assert.All(newSpots, ca =>
            Assert.IsType<Box>(ca.Location));

        Assert.All(newSpots, ca =>
            Assert.True(ca.NumCopies < copies));
    }
}