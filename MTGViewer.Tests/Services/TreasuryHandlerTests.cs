using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Services;

public class TreasuryHandlerTests : IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;

    public TreasuryHandlerTests(CardDbContext dbContext, TestDataGenerator testGen)
    {
        _dbContext = dbContext;
        _testGen = testGen;
    }


    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();


    private Task<int> GetTotalCopiesAsync() =>
        _dbContext.Amounts.SumAsync(amt => amt.NumCopies);


    private async Task RemoveCardCopiesAsync(Card card)
    {
        var boxCards = await _dbContext.Amounts
            .Where(a => a.Location is Box && a.Card.Name == card.Name)
            .ToListAsync();

        _dbContext.RemoveRange(boxCards);

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();
    }


    [Fact]
    public async Task AddCards_NullDbContext_Throws()
    {
        const CardDbContext nullDbContext = null!;
        IEnumerable<CardRequest> emptyRequests = Enumerable.Empty<CardRequest>();

        Task AddAsync() => nullDbContext.AddCardsAsync(emptyRequests);

        await Assert.ThrowsAsync<ArgumentNullException>(AddAsync);
    }


    [Fact]
    public async Task AddCards_NullRequests_Throws()
    {
        const IEnumerable<CardRequest> nullRequests = null!;

        Task AddAsync() => _dbContext.AddCardsAsync(nullRequests);

        await Assert.ThrowsAsync<ArgumentNullException>(AddAsync);
    }


    [Fact]
    public async Task AddCards_WithNullCardRequest_Throws()
    {
        var withNull = new CardRequest[] { null! };

        Task AddAsync() => _dbContext.AddCardsAsync(withNull);

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
    public async Task AddCards_NewCard_OnlyNew()
    {
        const int amount = 2;

        var card = await _dbContext.Amounts
            .Where(a => a.Location is Box)
            .Select(a => a.Card)
            .FirstAsync();

        await RemoveCardCopiesAsync(card);

        int totalBefore = await GetTotalCopiesAsync();

        await _dbContext.AddCardsAsync(card, amount);

        bool noModified = _dbContext.ChangeTracker
            .Entries<Amount>()
            .All(e => e.State is not EntityState.Modified);

        await _dbContext.SaveChangesAsync();

        int totalAfter = await GetTotalCopiesAsync();

        Assert.True(noModified);
        Assert.Equal(amount, totalAfter - totalBefore);
    }


    [Fact]
    public async Task AddCards_ExistingWithCapcity_OnlyExisting()
    {
        var card = await _dbContext.Amounts
            .Where(a => a.Location is Box)
            .Select(a => a.Card)
            .AsNoTracking()
            .FirstAsync();

        int remainingSpace = await _dbContext.Boxes
            .Where(b => b.Cards.Any(amt => amt.CardId == card.Id))
            .Select(b => b.Capacity - b.Cards.Sum(amt => amt.NumCopies))
            .SumAsync();

        int totalBefore = await GetTotalCopiesAsync();

        await _dbContext.AddCardsAsync(card, remainingSpace);

        bool noAdded = _dbContext.ChangeTracker
            .Entries<Amount>()
            .All(e => e.State is not EntityState.Added);

        await _dbContext.SaveChangesAsync();

        int totalAfter = await GetTotalCopiesAsync();

        Assert.True(noAdded);
        Assert.Equal(remainingSpace, totalAfter - totalBefore);
    }


    [Fact]
    public async Task AddCards_ExistingLackCapacity_MixDeposits()
    {
        const int modAmount = 5;

        var card = await _dbContext.Amounts
            .Where(a => a.Location is Box)
            .Select(a => a.Card)
            .AsNoTracking()
            .FirstAsync();

        int remainingSpace = await _dbContext.Boxes
            .Where(b => b.Cards.Any(amt => amt.CardId == card.Id))
            .Select(b => b.Capacity - b.Cards.Sum(amt => amt.NumCopies))
            .SumAsync();

        int requestAmount = remainingSpace + modAmount;
        int totalBefore = await GetTotalCopiesAsync();

        await _dbContext.AddCardsAsync(card, requestAmount);

        bool allAdded = _dbContext.ChangeTracker
            .Entries<Amount>()
            .All(e => e.State is EntityState.Added);

        bool allModified = _dbContext.ChangeTracker
            .Entries<Amount>()
            .All(e => e.State is EntityState.Modified);

        await _dbContext.SaveChangesAsync();

        int totalAfter = await GetTotalCopiesAsync();

        Assert.True(!allAdded && !allModified);
        Assert.Equal(requestAmount, totalAfter - totalBefore);
    }


    private async Task AddExcessAsync(int excessSpace)
    {
        int capacity = await _dbContext.Boxes
            .Where(b => !b.IsExcess)
            .SumAsync(b => b.Capacity);

        int availAmounts = await _dbContext.Boxes
            .Where(b => !b.IsExcess)
            .SelectMany(b => b.Cards)
            .SumAsync(a => a.NumCopies);

        if (availAmounts > capacity)
        {
            throw new InvalidOperationException("There are too many cards not in excess");
        }

        var card = await _dbContext.Cards.FirstAsync();

        if (availAmounts < capacity)
        {
            var boxes = await _dbContext.Boxes
                .Where(b => !b.IsExcess)
                .Include(b => b.Cards)
                .ToListAsync();

            foreach (var box in boxes)
            {
                int remaining = box.Capacity - box.Cards.Sum(a => a.NumCopies);
                if (remaining <= 0)
                {
                    continue;
                }

                if (box.Cards.FirstOrDefault() is Amount amount)
                {
                    amount.NumCopies += remaining;
                    continue;
                }

                amount = new Amount
                {
                    Card = card,
                    Location = box,
                    NumCopies = remaining
                };

                _dbContext.Amounts.Attach(amount);
            }
        }

        if (await _dbContext.Boxes.AnyAsync(b => b.IsExcess))
        {
            return;
        }

        var excess = new Box
        {
            Name = "Excess",
            Capacity = 0,
            Bin = new Bin
            {
                Name = "Excess Bin"
            }
        };

        var excessCard = new Amount
        {
            Card = card,
            Location = excess,
            NumCopies = excessSpace
        };

        _dbContext.Boxes.Attach(excess);
        _dbContext.Amounts.Attach(excessCard);

        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();
    }


    [Fact]
    public async Task Exchange_NullDbContext_Throws()
    {
        const CardDbContext nullDbContext = null!;
        var deck = new Deck();

        Task ExchangeAsync() => nullDbContext.ExchangeAsync(deck);

        await Assert.ThrowsAsync<ArgumentNullException>(ExchangeAsync);
    }


    [Fact]
    public async Task Exchange_NullDeck_Throws()
    {
        const Deck nullDeck = null!;

        Task ExchangeAsync() => _dbContext.ExchangeAsync(nullDeck);

        await Assert.ThrowsAsync<ArgumentNullException>(ExchangeAsync);
    }


    [Fact]
    public async Task UpdateBoxes_NullDbContext_Throws()
    {
        const CardDbContext nullDbContext = null!;

        Task UpdateAsync() => nullDbContext.UpdateBoxesAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(UpdateAsync);
    }


    [Fact]
    public async Task UpdateBoxes_NewBox_DecreaseExcess()
    {
        const int extraSpace = 15;

        await AddExcessAsync(extraSpace);

        int oldAvailable = await _dbContext.Boxes
            .Where(b => !b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

        int oldExcess = await _dbContext.Boxes
            .Where(b => b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

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
            .Where(b => !b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

        int newExcess = await _dbContext.Boxes
            .Where(b => b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

        Assert.Equal(extraSpace, newAvailable - oldAvailable);
        Assert.Equal(extraSpace, oldExcess - newExcess);
    }


    [Fact]
    public async Task UpdateBoxes_IncreaseCapacity_DecreaseExcess()
    {
        const int extraSpace = 15;

        await AddExcessAsync(extraSpace);

        int oldAvailable = await _dbContext.Boxes
            .Where(b => !b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

        int oldExcess = await _dbContext.Boxes
            .Where(b => b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

        var higherBox = await _dbContext.Boxes.FirstAsync();

        higherBox.Capacity += extraSpace;

        await _dbContext.UpdateBoxesAsync();

        await _dbContext.SaveChangesAsync();

        int newAvailable = await _dbContext.Boxes
            .Where(b => !b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

        int newExcess = await _dbContext.Boxes
            .Where(b => b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

        Assert.Equal(extraSpace, newAvailable - oldAvailable);
        Assert.Equal(extraSpace, oldExcess - newExcess);
    }


    [Fact]
    public async Task Update_DecreaseCapacity_IncreaseExcess()
    {
        const int extraSpace = 15;

        await AddExcessAsync(extraSpace);

        int oldAvailable = await _dbContext.Boxes
            .Where(b => !b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

        int oldExcess = await _dbContext.Boxes
            .Where(b => b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

        var lowerBox = await _dbContext.Boxes.FirstAsync();

        lowerBox.Capacity -= extraSpace;

        await _dbContext.UpdateBoxesAsync();

        await _dbContext.SaveChangesAsync();

        int newAvailable = await _dbContext.Boxes
            .Where(b => !b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

        int newExcess = await _dbContext.Boxes
            .Where(b => b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

        Assert.Equal(extraSpace, oldAvailable - newAvailable);
        Assert.Equal(extraSpace, newExcess - oldExcess);
    }
}
