using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Xunit;

using MtgViewer.Data;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Services;

public class CardDbContextTests : IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;

    public CardDbContextTests(CardDbContext dbContext, TestDataGenerator testGen)
    {
        _dbContext = dbContext;
        _testGen = testGen;
    }

    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();

    private async Task<int> GetTotalCopiesAsync()
        => await _dbContext.Holds.SumAsync(amt => amt.Copies);

    private async Task RemoveCardCopiesAsync(Card card)
    {
        var boxCards = await _dbContext.Holds
            .Where(h => h.Location is Box && h.Card.Name == card.Name)
            .ToListAsync();

        _dbContext.RemoveRange(boxCards);

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task AddCards_NullRequests_Throws()
    {
        const IEnumerable<CardRequest> nullRequests = null!;

        Task AddAsync() => _dbContext.AddCardsAsync(nullRequests);

        await Assert.ThrowsAsync<ArgumentNullException>(AddAsync);
    }

    [Fact]
    public async Task AddCards_WithNullCard_Throws()
    {
        const Card nullCard = null!;

        Task AddAsync() => _dbContext.AddCardsAsync(nullCard, 0);

        await Assert.ThrowsAsync<ArgumentNullException>(AddAsync);
    }

    [Fact]
    public async Task AddCards_WithNullCardRequest_NoChange()
    {
        var withNull = new CardRequest[] { null! };

        int totalBefore = await GetTotalCopiesAsync();

        await _dbContext.AddCardsAsync(withNull);

        int totalAfter = await GetTotalCopiesAsync();

        Assert.Equal(totalBefore, totalAfter);
    }

    [Fact]
    public async Task AddCards_EmptyCardRequest_NoChange()
    {
        var empty = Array.Empty<CardRequest>();

        int totalBefore = await GetTotalCopiesAsync();

        await _dbContext.AddCardsAsync(empty);

        int totalAfter = await GetTotalCopiesAsync();

        Assert.Equal(totalBefore, totalAfter);
    }

    [Fact]
    public async Task AddCards_NewCard_OnlyNew()
    {
        const int copies = 2;

        var card = await _dbContext.Holds
            .Where(h => h.Location is Box)
            .Select(h => h.Card)
            .FirstAsync();

        await RemoveCardCopiesAsync(card);

        int totalBefore = await GetTotalCopiesAsync();

        await _dbContext.AddCardsAsync(card, copies);

        bool noModified = _dbContext.ChangeTracker
            .Entries<Hold>()
            .All(e => e.State is not EntityState.Modified);

        await _dbContext.SaveChangesAsync();

        int totalAfter = await GetTotalCopiesAsync();

        Assert.True(noModified);
        Assert.Equal(copies, totalAfter - totalBefore);
    }

    [Fact]
    public async Task AddCards_ExistingWithCapacity_OnlyExisting()
    {
        var card = await _dbContext.Holds
            .Where(h => h.Location is Box)
            .Select(h => h.Card)
            .AsNoTracking()
            .FirstAsync();

        int remainingSpace = await _dbContext.Boxes
            .Where(b => b.Holds.Any(amt => amt.CardId == card.Id))
            .Select(b => b.Capacity - b.Holds.Sum(amt => amt.Copies))
            .SumAsync();

        int totalBefore = await GetTotalCopiesAsync();

        await _dbContext.AddCardsAsync(card, remainingSpace);

        bool noAdded = _dbContext.ChangeTracker
            .Entries<Hold>()
            .All(e => e.State is not EntityState.Added);

        await _dbContext.SaveChangesAsync();

        int totalAfter = await GetTotalCopiesAsync();

        Assert.True(noAdded);
        Assert.Equal(remainingSpace, totalAfter - totalBefore);
    }

    [Fact]
    public async Task AddCards_ExistingLackCapacity_MixDeposits()
    {
        const int modCopies = 5;

        var card = await _dbContext.Holds
            .Where(h => h.Location is Box)
            .Select(h => h.Card)
            .AsNoTracking()
            .FirstAsync();

        int remainingSpace = await _dbContext.Boxes
            .Where(b => b.Holds.Any(amt => amt.CardId == card.Id))
            .Select(b => b.Capacity - b.Holds.Sum(amt => amt.Copies))
            .SumAsync();

        int requestCopies = remainingSpace + modCopies;
        int totalBefore = await GetTotalCopiesAsync();

        await _dbContext.AddCardsAsync(card, requestCopies);

        bool allAdded = _dbContext.ChangeTracker
            .Entries<Hold>()
            .All(e => e.State is EntityState.Added);

        bool allModified = _dbContext.ChangeTracker
            .Entries<Hold>()
            .All(e => e.State is EntityState.Modified);

        await _dbContext.SaveChangesAsync();

        int totalAfter = await GetTotalCopiesAsync();

        Assert.True(!allAdded && !allModified);
        Assert.Equal(requestCopies, totalAfter - totalBefore);
    }

    [Fact]
    public async Task Exchange_NullDeck_Throws()
    {
        const Deck nullDeck = null!;

        Task ExchangeAsync() => _dbContext.ExchangeAsync(nullDeck);

        await Assert.ThrowsAsync<ArgumentNullException>(ExchangeAsync);
    }

    [Fact]
    public async Task UpdateBoxes_NewBox_DecreaseExcess()
    {
        const int extraSpace = 15;

        await _testGen.AddExcessAsync(extraSpace);

        int oldAvailable = await _dbContext.Boxes
            .SumAsync(b => b.Holds.Sum(h => h.Copies));

        int oldExcess = await _dbContext.Excess
            .SumAsync(b => b.Holds.Sum(h => h.Copies));

        var bin = await _dbContext.Bins.LastAsync();
        var newBox = new Box
        {
            Name = "Extra Box",
            Bin = bin,
            Capacity = extraSpace
        };

        _dbContext.Boxes.Add(newBox);

        await _dbContext.UpdateBoxesAsync();

        await _dbContext.SaveChangesAsync();

        int newAvailable = await _dbContext.Boxes
            .SumAsync(b => b.Holds.Sum(h => h.Copies));

        int newExcess = await _dbContext.Excess
            .SumAsync(b => b.Holds.Sum(h => h.Copies));

        Assert.Equal(extraSpace, newAvailable - oldAvailable);
        Assert.Equal(extraSpace, oldExcess - newExcess);
    }

    [Fact]
    public async Task UpdateBoxes_IncreaseCapacity_DecreaseExcess()
    {
        const int extraSpace = 15;

        await _testGen.AddExcessAsync(extraSpace);

        int oldAvailable = await _dbContext.Boxes
            .SumAsync(b => b.Holds.Sum(h => h.Copies));

        int oldExcess = await _dbContext.Excess
            .SumAsync(b => b.Holds.Sum(h => h.Copies));

        var higherBox = await _dbContext.Boxes.FirstAsync();

        higherBox.Capacity += extraSpace;

        await _dbContext.UpdateBoxesAsync();

        await _dbContext.SaveChangesAsync();

        int newAvailable = await _dbContext.Boxes
            .SumAsync(b => b.Holds.Sum(h => h.Copies));

        int newExcess = await _dbContext.Excess
            .SumAsync(b => b.Holds.Sum(h => h.Copies));

        Assert.Equal(extraSpace, newAvailable - oldAvailable);
        Assert.Equal(extraSpace, oldExcess - newExcess);
    }

    [Fact]
    public async Task Update_DecreaseCapacity_IncreaseExcess()
    {
        const int extraSpace = 15;

        await _testGen.AddExcessAsync(extraSpace);

        int oldAvailable = await _dbContext.Boxes
            .SumAsync(b => b.Holds.Sum(h => h.Copies));

        int oldExcess = await _dbContext.Excess
            .SumAsync(b => b.Holds.Sum(h => h.Copies));

        var lowerBox = await _dbContext.Boxes.FirstAsync();

        lowerBox.Capacity -= extraSpace;

        await _dbContext.UpdateBoxesAsync();

        await _dbContext.SaveChangesAsync();

        int newAvailable = await _dbContext.Boxes
            .SumAsync(b => b.Holds.Sum(h => h.Copies));

        int newExcess = await _dbContext.Excess
            .SumAsync(b => b.Holds.Sum(h => h.Copies));

        Assert.Equal(extraSpace, oldAvailable - newAvailable);
        Assert.Equal(extraSpace, newExcess - oldExcess);
    }
}
