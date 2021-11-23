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

        var boxCards = await _dbContext.Amounts
            .Where(ca => ca.Location is Box && ca.Card.Name == card.Name)
            .ToListAsync();

        _dbContext.RemoveRange(boxCards);

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();

        var results = await _treasuryQuery.FindCheckoutAsync(card, amount);

        Assert.Empty(results);
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

        var incompleteCopies = totalAmount + unfulfilled;

        var results = await _treasuryQuery.FindCheckoutAsync(card, incompleteCopies);

        var boxIds = results
            .Select(ex => ex.AmountId)
            .ToArray();

        var cardNames = await _dbContext.Amounts
            .Where(ca => boxIds.Contains(ca.Id))
            .Select(ca => ca.Card.Name)
            .ToListAsync();

        Assert.All(cardNames, name => 
            Assert.Equal(card.Name, name));

        Assert.Equal(totalAmount, results.Sum(rr => rr.NumCopies));
    }


    [Fact]
    public async Task FindCheckout_SatisfyCopies_CompleteResult()
    {
        var card = await _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .Select(ca => ca.Card)
            .AsNoTracking()
            .FirstAsync();

        var totalAmount = await _dbContext.Amounts
            .Where(ca => ca.Location is Box && ca.CardId == card.Id)
            .SumAsync(ca => ca.NumCopies);

        var results = await _treasuryQuery.FindCheckoutAsync(card, totalAmount);

        var boxIds = results
            .Select(ex => ex.AmountId)
            .ToArray();

        var cardIds = await _dbContext.Amounts
            .Where(ca => boxIds.Contains(ca.Id))
            .Select(ca => ca.CardId)
            .ToListAsync();

        Assert.All(cardIds, id =>
            Assert.Equal(card.Id, id));

        Assert.Equal(totalAmount, results.Sum(ex => ex.NumCopies));
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
    public async Task FindReturn_NewCard_OnlyExtension()
    {
        const int amount = 2;

        var card = await _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .Select(ca => ca.Card)
            .FirstAsync();

        var boxCards = await _dbContext.Amounts
            .Where(ca => ca.Location is Box && ca.Card.Name == card.Name)
            .ToListAsync();

        _dbContext.RemoveRange(boxCards);

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();

        var results = await _treasuryQuery.FindReturnAsync(card, amount);

        var additions = results
            .OfType<Extension>()
            .ToList();

        Assert.All(results, result =>
            Assert.IsType<Extension>(result));

        Assert.Equal(amount, additions.Sum(add => add.NumCopies));

        Assert.All(additions, add =>
            Assert.Equal(card.Id, add.CardId));
    }


    [Fact]
    public async Task FindReturn_ExistingWithCapcity_OnlyAddition()
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

        var results = await _treasuryQuery.FindReturnAsync(card, remainingSpace);

        var addition = results
            .OfType<Addition>()
            .ToList();

        var addAmtIds = addition
            .Select(ext => ext.AmountId)
            .ToArray();

        var addCardIds = await _dbContext.Amounts
            .Where(ca => addAmtIds.Contains(ca.Id))
            .Select(ca => ca.CardId)
            .ToListAsync();

        Assert.All(results, result =>
            Assert.IsType<Addition>(result));

        Assert.Equal(remainingSpace, addition.Sum(ext => ext.NumCopies));

        Assert.All(addCardIds, cardId =>
            Assert.Equal(card.Id, cardId));
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

        var results = await _treasuryQuery.FindReturnAsync(card, requestAmount);

        var extension = results
            .OfType<Extension>()
            .ToList();

        var addition = results
            .OfType<Addition>()
            .ToList();

        var addAmtIds = addition
            .Select(ext => ext.AmountId)
            .ToArray();

        var addCardIds = await _dbContext.Amounts
            .Where(ca => addAmtIds.Contains(ca.Id))
            .Select(ca => ca.CardId)
            .ToListAsync();

        Assert.All(addCardIds, cardId =>
            Assert.Equal(card.Id, cardId));

        Assert.All(extension, ext =>
            Assert.Equal(card.Id, ext.CardId));

        Assert.Equal(remainingSpace, addition.Sum(add => add.NumCopies));
        Assert.Equal(modAmount, extension.Sum(ext => ext.NumCopies));
    }
}