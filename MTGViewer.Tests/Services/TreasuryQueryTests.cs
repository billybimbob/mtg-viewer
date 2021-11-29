using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Services;

public class TreasuryQueryTests : IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly ITreasuryQuery _treasuryQuery;
    private readonly TestDataGenerator _testGen;

    public TreasuryQueryTests(
        CardDbContext dbContext,
        ITreasuryQuery treasuryQuery, 
        TestDataGenerator testGen)
    {
        _dbContext = dbContext;
        _treasuryQuery = treasuryQuery;
        _testGen = testGen;
    }


    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();


    private Task<int> GetTotalCopiesAsync() =>
        _dbContext.Amounts.SumAsync(amt => amt.NumCopies);

    private Task<int[]> GetAmountIdsAsync() =>
        _dbContext.Amounts
            .Select(ca => ca.Id)
            .OrderBy(id => id)
            .ToArrayAsync();


    private async Task RemoveCardCopiesAsync(Card card)
    {
        var boxCards = await _dbContext.Amounts
            .Where(ca => ca.Location is Box && ca.Card.Name == card.Name)
            .ToListAsync();

        _dbContext.RemoveRange(boxCards);

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();
    }


    private Task ApplyChangesAsync(IEnumerable<Amount> changes)
    {
        _dbContext.Amounts.UpdateRange(changes);

        return _dbContext.SaveChangesAsync();
    }


    [Fact]
    public async Task FindCheckout_NullRequests_Throws()
    {
        const IEnumerable<CardRequest> nullRequests = null!;

        Task FindAsync() => _treasuryQuery.FindCheckoutAsync(nullRequests);

        await Assert.ThrowsAsync<ArgumentNullException>(FindAsync);
    }


    [Fact]
    public async Task FindCheckout_WithNullCardRequest_Throws()
    {
        var withNull = new CardRequest[] { null! };

        Task FindAsync() => _treasuryQuery.FindCheckoutAsync(withNull);

        await Assert.ThrowsAsync<ArgumentNullException>(FindAsync);
    }


    [Fact]
    public async Task FindCheckout_WithNullCard_Throws()
    {
        const Card nullCard = null!;

        Task FindAsync() => _treasuryQuery.FindCheckoutAsync(nullCard, 0);

        await Assert.ThrowsAsync<ArgumentNullException>(FindAsync);
    }


    [Fact]
    public async Task FindCheckout_MissingCardName_EmptyResult()
    {
        const int amount = 1;

        var card = await _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .Select(ca => ca.Card)
            .FirstAsync();

        await RemoveCardCopiesAsync(card);

        var (checkouts, originals) = await _treasuryQuery.FindCheckoutAsync(card, amount);

        Assert.Empty(checkouts);
        Assert.Empty(originals);
    }


    // [Fact]
    // public async Task FindCheckout_MissingExactCard_CloseResults()
    // {
    //     var card = await _dbContext.Amounts
    //         .Where(ca => ca.Location is Box)
    //         .Select(ca => ca.Card)
    //         .FirstAsync();

    //     var boxCards = _dbContext.Amounts
    //         .Where(ca => ca.Location is Box && ca.CardId == card.Id);
    // }


    [Fact]
    public async Task FindCheckout_LackCopies_IncompleteResult()
    {
        const int unfulfilled = 2;

        var card = await _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .Select(ca => ca.Card)
            .AsNoTracking()
            .FirstAsync();

        var totalAmount = await _dbContext.Amounts
            .Where(ca => ca.Location is Box && ca.Card.Name == card.Name)
            .SumAsync(ca => ca.NumCopies);

        int incompleteCopies = totalAmount + unfulfilled;
        int totalBefore = await GetTotalCopiesAsync();

        var (checkouts, _) = await _treasuryQuery.FindCheckoutAsync(card, incompleteCopies);

        var amountIds = checkouts
            .Select(amt => amt.Id)
            .ToArray();

        var cardNames = await _dbContext.Amounts
            .Where(ca => amountIds.Contains(ca.Id))
            .Select(ca => ca.Card.Name)
            .ToListAsync();

        await ApplyChangesAsync(checkouts);
        int totalAfter = await GetTotalCopiesAsync();

        Assert.All(cardNames, name => 
            Assert.Equal(card.Name, name));

        Assert.Equal(totalAmount, totalBefore - totalAfter);
    }


    [Fact]
    public async Task FindCheckout_SatisfyCopies_CompleteResult()
    {
        var card = await _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .Select(ca => ca.Card)
            .AsNoTracking()
            .FirstAsync();

        int totalAmount = await _dbContext.Amounts
            .Where(ca => ca.Location is Box && ca.CardId == card.Id)
            .SumAsync(ca => ca.NumCopies);

        int totalBefore = await GetTotalCopiesAsync();
        var (checkouts, _) = await _treasuryQuery.FindCheckoutAsync(card, totalAmount);

        await ApplyChangesAsync(checkouts);
        int totalAfter = await GetTotalCopiesAsync();

        Assert.All(checkouts, amt =>
            Assert.Equal(card.Id, amt.CardId));

        Assert.Equal(totalAmount, totalBefore - totalAfter);
    }


    [Fact]
    public async Task FindReturn_NullRequests_Throws()
    {
        const IEnumerable<CardRequest> nullRequests = null!;

        Task FindAsync() => _treasuryQuery.FindReturnAsync(nullRequests);

        await Assert.ThrowsAsync<ArgumentNullException>(FindAsync);
    }


    [Fact]
    public async Task FindReturn_WithNullCardRequest_Throws()
    {
        var withNull = new CardRequest[] { null! };

        Task FindAsync() => _treasuryQuery.FindReturnAsync(withNull);

        await Assert.ThrowsAsync<ArgumentNullException>(FindAsync);
    }


    [Fact]
    public async Task FindReturn_WithNullCard_Throws()
    {
        const Card nullCard = null!;

        Task FindAsync() => _treasuryQuery.FindReturnAsync(nullCard, 0);

        await Assert.ThrowsAsync<ArgumentNullException>(FindAsync);
    }


    [Fact]
    public async Task FindReturn_NewCard_OnlyNew()
    {
        const int amount = 2;

        var card = await _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .Select(ca => ca.Card)
            .FirstAsync();

        await RemoveCardCopiesAsync(card);

        var (additions, originals) = await _treasuryQuery.FindReturnAsync(card, amount);

        var addIds = additions
            .Select(add => add.Id)
            .ToArray();

        bool anyExist = await _dbContext.Amounts
            .AnyAsync(ca => addIds.Contains(ca.Id));

        int newTotal = additions.Sum(amt => amt.NumCopies);

        Assert.NotEmpty(additions);
        Assert.Empty(originals);
        Assert.False(anyExist);

        Assert.All(additions, add =>
            Assert.Equal(card.Id, add.CardId));

        Assert.Equal(amount, newTotal);
    }


    [Fact]
    public async Task FindReturn_ExistingWithCapcity_OnlyExisting()
    {
        var card = await _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .Select(ca => ca.Card)
            .AsNoTracking()
            .FirstAsync();

        int remainingSpace = await _dbContext.Boxes
            .Where(b => b.Cards.Any(amt => amt.CardId == card.Id))
            .Select(b => b.Capacity - b.Cards.Sum(amt => amt.NumCopies))
            .SumAsync();

        int totalBefore = await GetTotalCopiesAsync();
        var dbIds = await GetAmountIdsAsync();

        var (additions, originals) = await _treasuryQuery.FindReturnAsync(card, remainingSpace);

        bool anyNew = additions
            .ExceptBy(dbIds, add => add.Id)
            .Any();

        await ApplyChangesAsync(additions);

        int totalAfter = await GetTotalCopiesAsync();

        Assert.NotEmpty(additions);
        Assert.False(anyNew);

        Assert.All(originals.Keys, id =>
            Assert.Contains(id, dbIds));

        Assert.All(additions, amt =>
            Assert.Equal(card.Id, amt.CardId));

        Assert.Equal(remainingSpace, totalAfter - totalBefore);
    }


    [Fact]
    public async Task FindReturn_ExistingLackCapacity_MixDeposits()
    {
        const int modAmount = 5;

        var card = await _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .Select(ca => ca.Card)
            .AsNoTracking()
            .FirstAsync();

        int remainingSpace = await _dbContext.Boxes
            .Where(b => b.Cards.Any(amt => amt.CardId == card.Id))
            .Select(b => b.Capacity - b.Cards.Sum(amt => amt.NumCopies))
            .SumAsync();

        int requestAmount = remainingSpace + modAmount;

        int totalBefore = await GetTotalCopiesAsync();
        var dbIds = await GetAmountIdsAsync();

        var (additions, originals) = await _treasuryQuery.FindReturnAsync(card, requestAmount);

        int newTotal = additions
            .ExceptBy(dbIds, add => add.Id)
            .Sum(amt => amt.NumCopies);

        await ApplyChangesAsync(additions);

        int totalAfter = await GetTotalCopiesAsync();

        Assert.All(originals.Keys, id =>
            Assert.Contains(id, dbIds));

        Assert.All(additions, amt =>
            Assert.Equal(card.Id, amt.CardId));

        Assert.Equal(modAmount, newTotal);
        Assert.Equal(requestAmount, totalAfter - totalBefore);
    }
}
