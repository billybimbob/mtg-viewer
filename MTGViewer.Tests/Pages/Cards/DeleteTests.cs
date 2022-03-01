using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Cards;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Cards;

public class DeleteTests : IAsyncLifetime
{
    private readonly DeleteModel _deleteModel;
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;

    public DeleteTests(
        DeleteModel deleteModel,
        CardDbContext dbContext,
        TestDataGenerator testGen)
    {
        _deleteModel = deleteModel;
        _dbContext = dbContext;
        _testGen = testGen;
    }


    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();
    

    [Fact]
    public async Task OnPost_NullInput_NotFound()
    {
        _deleteModel.Input = null;

        var result = await _deleteModel.OnPostAsync(null!, null, default);

        Assert.IsType<NotFoundResult>(result);
    }


    [Fact]
    public async Task OnPost_NullId_NotFound()
    {
        _deleteModel.Input = new DeleteModel.InputModel
        {
            RemoveCopies = 0
        };

        var result = await _deleteModel.OnPostAsync(null!, null, default);

        Assert.IsType<NotFoundResult>(result);
    }


    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task OnPost_NegativeAmount_NoChange(int amount)
    {
        var cardId = await _dbContext.Cards.Select(c => c.Id).FirstAsync();

        _deleteModel.Input = new DeleteModel.InputModel
        {
            RemoveCopies = amount
        };

        int oldCopies = await _dbContext.Amounts
            .Where(a => a.CardId == cardId && a.Location is Box)
            .SumAsync(a => a.NumCopies);

        var result = await _deleteModel.OnPostAsync(cardId, null, default);

        int newCopies = await _dbContext.Amounts
            .Where(a => a.CardId == cardId && a.Location is Box)
            .SumAsync(a => a.NumCopies);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(oldCopies, newCopies);
    }


    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task OnPost_ValidAmount_Lowers(int amount)
    {
        var cardId = await _dbContext.Amounts
            .Where(a => a.NumCopies > 0 && a.Location is Box)
            .Select(a => a.CardId)
            .FirstAsync();

        int oldCopies = await _dbContext.Amounts
            .Where(a => a.CardId == cardId && a.Location is Box)
            .SumAsync(a => a.NumCopies);

        amount = Math.Min(amount, oldCopies);

        _deleteModel.Input = new DeleteModel.InputModel
        {
            RemoveCopies = amount
        };

        var result = await _deleteModel.OnPostAsync(cardId, null, default);

        int newCopies = await _dbContext.Amounts
            .Where(a => a.CardId == cardId && a.Location is Box)
            .SumAsync(a => a.NumCopies);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(amount, oldCopies - newCopies);
    }


    [Fact]
    public async Task OnPost_MaxAmount_RemoveCard()
    {
        var cardId = await _dbContext.Amounts
            .Where(a => a.NumCopies > 0 && a.Location is Box)
            .Select(a => a.CardId)
            .FirstAsync();

        var deckAmounts = await _dbContext.Amounts
            .Where(a => a.Location is Deck && a.CardId == cardId)
            .ToListAsync();

        _dbContext.Amounts.RemoveRange(deckAmounts);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        int oldCopies = await _dbContext.Amounts
            .Where(a => a.CardId == cardId && a.Location is Box)
            .SumAsync(a => a.NumCopies);

        _deleteModel.Input = new DeleteModel.InputModel
        {
            RemoveCopies = oldCopies
        };

        var result = await _deleteModel.OnPostAsync(cardId, null, default);

        int newCopies = await _dbContext.Amounts
            .Where(a => a.CardId == cardId && a.Location is Box)
            .SumAsync(a => a.NumCopies);

        bool cardRemains = await _dbContext.Cards
            .AnyAsync(c => c.Id == cardId);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(0, newCopies);
        Assert.False(cardRemains);
    }


    [Fact]
    public async Task OnPost_MaxAmountWithDeck_CardRemains()
    {
        var cardId = await _dbContext.Amounts
            .Where(a => a.NumCopies > 0 && a.Location is Box)
            .Select(a => a.CardId)
            .FirstAsync();

        bool hasDeckAmounts = await _dbContext.Amounts
            .AnyAsync(a => a.Location is Deck && a.CardId == cardId);

        if (!hasDeckAmounts)
        {
            var deck = await _dbContext.Decks.FirstAsync();

            _dbContext.Amounts.Add(new Amount
            {
                CardId = cardId,
                Location = deck,
                NumCopies = 4
            });

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
        }

        int oldCopies = await _dbContext.Amounts
            .Where(a => a.CardId == cardId && a.Location is Box)
            .SumAsync(a => a.NumCopies);

        _deleteModel.Input = new DeleteModel.InputModel
        {
            RemoveCopies = oldCopies
        };

        var result = await _deleteModel.OnPostAsync(cardId, null, default);

        int newCopies = await _dbContext.Amounts
            .Where(a => a.CardId == cardId && a.Location is Box)
            .SumAsync(a => a.NumCopies);

        bool cardRemains = await _dbContext.Cards
            .AnyAsync(c => c.Id == cardId);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(0, newCopies);
        Assert.True(cardRemains);
    }


    [Fact]
    public async Task OnPost_HasExcess_ExcessTranferred()
    {
        await _testGen.AddExcessAsync(15);

        var cardId = await _dbContext.Cards
            .Where(c => c.Amounts
                .Any(a => a.Location is Excess))
            .Select(c => c.Id)
            .FirstAsync();

        var deckAmounts = await _dbContext.Amounts
            .Where(a => a.Location is Deck && a.CardId == cardId)
            .ToListAsync();

        _dbContext.Amounts.RemoveRange(deckAmounts);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        int removeCopies = await _dbContext.Amounts
            .Where(a => a.CardId == cardId)
            .SumAsync(a => a.NumCopies);

        _deleteModel.Input = new DeleteModel.InputModel
        {
            RemoveCopies = removeCopies
        };

        int boxesOld = await _dbContext.Boxes
            .SelectMany(b => b.Cards)
            .SumAsync(a => a.NumCopies);

        int excessOld = await _dbContext.Excess
            .SelectMany(b => b.Cards)
            .SumAsync(a => a.NumCopies);

        var result = await _deleteModel.OnPostAsync(cardId, null, default);

        int boxesNew = await _dbContext.Boxes
            .SelectMany(b => b.Cards)
            .SumAsync(a => a.NumCopies);

        int excessNew = await _dbContext.Excess
            .SelectMany(b => b.Cards)
            .SumAsync(a => a.NumCopies);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(removeCopies, boxesOld - boxesNew + excessOld - excessNew);
        Assert.InRange(excessOld - excessNew, 1, removeCopies);
    }
}