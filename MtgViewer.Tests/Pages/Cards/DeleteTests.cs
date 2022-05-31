using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MtgViewer.Data;
using MtgViewer.Pages.Cards;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Pages.Cards;

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
    public async Task OnPost_NegativeCopies_NoChange(int copies)
    {
        string cardId = await _dbContext.Cards.Select(c => c.Id).FirstAsync();

        _deleteModel.Input = new DeleteModel.InputModel
        {
            RemoveCopies = copies
        };

        int oldCopies = await _dbContext.Holds
            .Where(h => h.CardId == cardId && h.Location is Box)
            .SumAsync(h => h.Copies);

        var result = await _deleteModel.OnPostAsync(cardId, null, default);

        int newCopies = await _dbContext.Holds
            .Where(h => h.CardId == cardId && h.Location is Box)
            .SumAsync(h => h.Copies);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(oldCopies, newCopies);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task OnPost_ValidCopies_Lowers(int copies)
    {
        string cardId = await _dbContext.Holds
            .Where(h => h.Copies > 0 && h.Location is Box)
            .Select(h => h.CardId)
            .FirstAsync();

        int oldCopies = await _dbContext.Holds
            .Where(h => h.CardId == cardId && h.Location is Box)
            .SumAsync(h => h.Copies);

        copies = Math.Min(copies, oldCopies);

        _deleteModel.Input = new DeleteModel.InputModel
        {
            RemoveCopies = copies
        };

        var result = await _deleteModel.OnPostAsync(cardId, null, default);

        int newCopies = await _dbContext.Holds
            .Where(h => h.CardId == cardId && h.Location is Box)
            .SumAsync(h => h.Copies);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(copies, oldCopies - newCopies);
    }

    [Fact]
    public async Task OnPost_MaxCopies_RemoveCard()
    {
        string cardId = await _dbContext.Holds
            .Where(h => h.Copies > 0 && h.Location is Box)
            .Select(h => h.CardId)
            .FirstAsync();

        var deckHolds = await _dbContext.Holds
            .Where(h => h.Location is Deck && h.CardId == cardId)
            .ToListAsync();

        _dbContext.Holds.RemoveRange(deckHolds);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        int oldCopies = await _dbContext.Holds
            .Where(h => h.CardId == cardId && h.Location is Box)
            .SumAsync(h => h.Copies);

        _deleteModel.Input = new DeleteModel.InputModel
        {
            RemoveCopies = oldCopies
        };

        var result = await _deleteModel.OnPostAsync(cardId, null, default);

        int newCopies = await _dbContext.Holds
            .Where(h => h.CardId == cardId && h.Location is Box)
            .SumAsync(h => h.Copies);

        bool cardRemains = await _dbContext.Cards
            .AnyAsync(c => c.Id == cardId);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(0, newCopies);
        Assert.False(cardRemains);
    }

    [Fact]
    public async Task OnPost_MaxCopiesWithDeck_CardRemains()
    {
        string cardId = await _dbContext.Holds
            .Where(h => h.Copies > 0 && h.Location is Box)
            .Select(h => h.CardId)
            .FirstAsync();

        bool hasDeckHolds = await _dbContext.Holds
            .AnyAsync(h => h.Location is Deck && h.CardId == cardId);

        if (!hasDeckHolds)
        {
            var deck = await _dbContext.Decks.FirstAsync();

            _dbContext.Holds.Add(new Hold
            {
                CardId = cardId,
                Location = deck,
                Copies = 4
            });

            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
        }

        int oldCopies = await _dbContext.Holds
            .Where(h => h.CardId == cardId && h.Location is Box)
            .SumAsync(h => h.Copies);

        _deleteModel.Input = new DeleteModel.InputModel
        {
            RemoveCopies = oldCopies
        };

        var result = await _deleteModel.OnPostAsync(cardId, null, default);

        int newCopies = await _dbContext.Holds
            .Where(h => h.CardId == cardId && h.Location is Box)
            .SumAsync(h => h.Copies);

        bool cardRemains = await _dbContext.Cards
            .AnyAsync(c => c.Id == cardId);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(0, newCopies);
        Assert.True(cardRemains);
    }

    [Fact]
    public async Task OnPost_HasExcess_ExcessTransferred()
    {
        await _testGen.AddExcessAsync(15);

        string cardId = await _dbContext.Cards
            .Where(c => c.Holds
                .Any(h => h.Location is Excess))
            .Select(c => c.Id)
            .FirstAsync();

        var deckHolds = await _dbContext.Holds
            .Where(h => h.Location is Deck && h.CardId == cardId)
            .ToListAsync();

        _dbContext.Holds.RemoveRange(deckHolds);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        int removeCopies = await _dbContext.Holds
            .Where(h => h.CardId == cardId)
            .SumAsync(h => h.Copies);

        _deleteModel.Input = new DeleteModel.InputModel
        {
            RemoveCopies = removeCopies
        };

        int boxesOld = await _dbContext.Boxes
            .SelectMany(b => b.Holds)
            .SumAsync(h => h.Copies);

        int excessOld = await _dbContext.Excess
            .SelectMany(b => b.Holds)
            .SumAsync(h => h.Copies);

        var result = await _deleteModel.OnPostAsync(cardId, null, default);

        int boxesNew = await _dbContext.Boxes
            .SelectMany(b => b.Holds)
            .SumAsync(h => h.Copies);

        int excessNew = await _dbContext.Excess
            .SelectMany(b => b.Holds)
            .SumAsync(h => h.Copies);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(removeCopies, boxesOld - boxesNew + excessOld - excessNew);
        Assert.InRange(excessOld - excessNew, 1, removeCopies);
    }
}
