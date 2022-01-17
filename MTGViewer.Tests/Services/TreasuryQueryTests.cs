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
    public async Task RequestCheckout_NullRequests_Throws()
    {
        const IEnumerable<CardRequest> nullRequests = null!;

        Task FindAsync() => _treasuryQuery.RequestCheckoutAsync(nullRequests);

        await Assert.ThrowsAsync<ArgumentNullException>(FindAsync);
    }


    [Fact]
    public async Task RequestCheckout_WithNullCardRequest_Throws()
    {
        var withNull = new CardRequest[] { null! };

        Task FindAsync() => _treasuryQuery.RequestCheckoutAsync(withNull);

        await Assert.ThrowsAsync<ArgumentNullException>(FindAsync);
    }


    [Fact]
    public async Task RequestCheckout_WithNullCard_Throws()
    {
        const Card nullCard = null!;

        Task FindAsync() => _treasuryQuery.FindCheckoutAsync(nullCard, 0);

        await Assert.ThrowsAsync<ArgumentNullException>(FindAsync);
    }


    [Fact]
    public async Task RequestCheckout_MissingCardName_EmptyResult()
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
    public async Task RequestCheckout_LackCopies_IncompleteResult()
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
    public async Task RequestCheckout_SatisfyCopies_CompleteResult()
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
    public async Task RequestReturn_NullRequests_Throws()
    {
        const IEnumerable<CardRequest> nullRequests = null!;

        Task FindAsync() => _treasuryQuery.RequestReturnAsync(nullRequests);

        await Assert.ThrowsAsync<ArgumentNullException>(FindAsync);
    }


    [Fact]
    public async Task RequestReturn_WithNullCardRequest_Throws()
    {
        var withNull = new CardRequest[] { null! };

        Task FindAsync() => _treasuryQuery.RequestReturnAsync(withNull);

        await Assert.ThrowsAsync<ArgumentNullException>(FindAsync);
    }


    [Fact]
    public async Task RequestReturn_WithNullCard_Throws()
    {
        const Card nullCard = null!;

        Task FindAsync() => _treasuryQuery.FindReturnAsync(nullCard, 0);

        await Assert.ThrowsAsync<ArgumentNullException>(FindAsync);
    }


    [Fact]
    public async Task RequestReturn_NewCard_OnlyNew()
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
    public async Task RequestReturn_ExistingWithCapcity_OnlyExisting()
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
    public async Task RequestReturn_ExistingLackCapacity_MixDeposits()
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


    private async Task AddExcessAsync(int excessSpace)
    {
        int capacity = await _dbContext.Boxes
            .Where(b => !b.IsExcess)
            .SumAsync(b => b.Capacity);

        int availAmounts = await _dbContext.Boxes
            .Where(b => !b.IsExcess)
            .SumAsync(b => b.Cards.Sum(a => a.NumCopies));

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
    public async Task RequestUpdate_NewBox_DecreaseExcess()
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

        var transfers = await _treasuryQuery.RequestUpdateAsync(newBox);

        _dbContext.Boxes.Add(newBox);
        _dbContext.AttachResult(transfers);

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
    public async Task RequestUpdate_IncreaseCapacity_DecreaseExcess()
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

        var transfers = await _treasuryQuery.RequestUpdateAsync(higherBox);

        _dbContext.Boxes.Update(higherBox);
        _dbContext.AttachResult(transfers);

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
    public async Task RequestUpdate_DecreaseCapacity_IncreaseExcess()
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

        var transfers = await _treasuryQuery.RequestUpdateAsync(lowerBox);

        _dbContext.Boxes.Update(lowerBox);
        _dbContext.AttachResult(transfers);

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
