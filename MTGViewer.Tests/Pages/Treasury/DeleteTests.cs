using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Treasury;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Treasury;

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
    public async Task OnPost_InvalidBox_NotFound()
    {
        var deckId = await _dbContext.Decks.Select(d => d.Id).FirstAsync();

        var result = await _deleteModel.OnPostAsync(deckId, default);

        Assert.IsType<NotFoundResult>(result);
    }


    [Fact]
    public async Task OnPost_ExcessBox_NotFound()
    {
        var excessBox = new Box
        {
            Name = "Excess",
            Capacity = 0
        };

        _dbContext.Boxes.Add(excessBox);
        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();

        var result = await _deleteModel.OnPostAsync(excessBox.Id, default);

        Assert.IsType<NotFoundResult>(result);
    }


    [Fact]
    public async Task OnPost_RandomBox_DeleteSuccess()
    {
        var randomBox = await _dbContext.Boxes.FirstAsync();

        var result = await _deleteModel.OnPostAsync(randomBox.Id, default);

        bool isDeleted = await _dbContext.Boxes.AllAsync(b => b.Id != randomBox.Id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.True(isDeleted);
    }


    [Fact]
    public async Task OnPost_WithCards_CardsPreserved()
    {
        var boxWithCards = await _dbContext.Boxes
            .FirstAsync(b => b.Holds.Any());

        int treasuryCardsBefore = await _dbContext.Holds
            .Where(h => h.Location is Box)
            .SumAsync(h => h.Copies);

        var result = await _deleteModel.OnPostAsync(boxWithCards.Id, default);

        bool isDeleted = await _dbContext.Boxes.AllAsync(b => b.Id != boxWithCards.Id);

        int treasuryCardsAfter = await _dbContext.Holds
            .Where(h => h.Location is Box)
            .SumAsync(h => h.Copies);

        Assert.IsType<RedirectToPageResult>(result);

        Assert.True(isDeleted);
        Assert.Equal(treasuryCardsBefore, treasuryCardsAfter);
    }


    [Fact]
    public async Task OnPost_LastInBin_BinDeleted()
    {
        var lastInBin = new Box
        {
            Name = "Last in Bin",
            Capacity = 20,
            Bin = new Bin
            {
                Name = "Last"
            }
        };

        _dbContext.Boxes.Add(lastInBin);
        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();

        bool binExistsBefore = await _dbContext.Bins
            .AnyAsync(b => b.Id == lastInBin.BinId);

        var result = await _deleteModel.OnPostAsync(lastInBin.Id, default);

        bool boxExistsAfter = await _dbContext.Boxes
            .AnyAsync(b => b.Id == lastInBin.Id);

        bool binExistsAfter = await _dbContext.Bins
            .AnyAsync(b => b.Id == lastInBin.BinId);

        Assert.True(binExistsBefore);

        Assert.IsType<RedirectToPageResult>(result);

        Assert.False(boxExistsAfter);
        Assert.False(binExistsAfter);
    }
}