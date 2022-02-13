using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests;

public class SeekListTests : IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;

    public SeekListTests(CardDbContext dbContext, TestDataGenerator testGen)
    {
        _dbContext = dbContext;
        _testGen = testGen;
    }


    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();


    [Fact]
    public async Task ToSeekList_OrderByIdFirst_ReturnsFirst()
    {
        var cards = _dbContext.Cards.OrderBy(c => c.Id);

        int pageSize = Math.Min(10, await cards.CountAsync() / 2);

        const string? firstId = null;

        var seekList = await cards
            .ToSeekListAsync(null, pageSize, firstId, false);

        var firstCards = await cards
            .Select(c => c.Id)
            .Take(pageSize)
            .ToListAsync();

        Assert.Null(seekList.Seek.Previous);
        Assert.NotNull(seekList.Seek.Next);

        Assert.Equal(pageSize, seekList.Count);
        Assert.Equal(firstCards, seekList.Select(c => c.Id));
    }


    [Fact]
    public async Task ToSeekList_NoOrderByFirst_ReturnsFirst()
    {
        var cards = _dbContext.Cards;

        int pageSize = Math.Min(10, await cards.CountAsync() / 2);

        const string? firstId = null;

        var seekList = await cards
            .ToSeekListAsync(null, pageSize, firstId, false);

        var firstCards = await cards
            .Select(c => c.Id)
            .Take(pageSize)
            .ToListAsync();

        Assert.Null(seekList.Seek.Previous);
        Assert.NotNull(seekList.Seek.Next);

        Assert.Equal(pageSize, seekList.Count);
        Assert.Equal(firstCards, seekList.Select(c => c.Id));
    }


    [Fact]
    public async Task ToSeekList_NoOrderBySeek_Throws()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards;

        var seek = await cards
            .Skip(pageSize)
            .Select(c => c.Id)
            .FirstAsync();

        Task SeekListAsync() => cards
            .ToSeekListAsync(0, pageSize, seek, false);

        await Assert.ThrowsAsync<InvalidOperationException>(SeekListAsync);
    }


    [Fact]
    public async Task ToSeekList_OrderBySeek_Returns()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards.OrderBy(c => c.Id);

        var seek = await cards
            .Skip(pageSize)
            .Select(c => c.Id)
            .FirstAsync();

        var seekList = await cards
            .ToSeekListAsync(1, pageSize, seek, false);

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(c.Id.CompareTo(seek) > 0));
    }


    [Fact]
    public async Task ToSeekList_OrderBySeekBackwards_Returns()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards.OrderBy(c => c.Id);

        var seek = await cards
            .Skip(pageSize)
            .Select(c => c.Id)
            .FirstAsync();

        var seekList = await cards
            .ToSeekListAsync(0, pageSize, seek, true);

        Assert.Null(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(c.Id.CompareTo(seek) < 0));
    }


    [Fact]
    public async Task ToSeekList_OrderBySeekMultiple_Returns()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var seek = await cards
            .Skip(pageSize * numPages)
            .FirstAsync();

        var seekList = await cards
            .ToSeekListAsync(numPages, pageSize, seek.Id, false);

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(
                (c.Name, c.Id).CompareTo((seek.Name, c.Id)) > 0));
    }


    [Fact]
    public async Task ToSeekList_OrderBySeekMultipleBackwards_Returns()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var seek = await cards
            .Skip(pageSize * numPages)
            .FirstAsync();

        var seekList = await cards
            .ToSeekListAsync(numPages - 1, pageSize, seek.Id, true);

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(
                (c.Name, c.Id).CompareTo((seek.Name, c.Id)) < 0));
    }


    [Fact]
    public async Task ToSeekList_OrderByManySeek_Returns()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenByDescending(c => c.Cmc)
                .ThenByDescending(c => c.Artist)
                .ThenBy(c => c.Id);

        var seek = await cards
            .Skip(pageSize)
            .FirstAsync();

        var seekList = await cards
            .ToSeekListAsync(1, pageSize, seek.Id, false);

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(
                c.Name.CompareTo(seek.Name) > 0

                || c.Name == seek.Name
                    && c.SetName.CompareTo(seek.SetName) > 0

                || c.Name == seek.Name
                    && c.SetName == seek.SetName
                    && c.Cmc < seek.Cmc

                || c.Name == seek.Name
                    && c.SetName == seek.SetName
                    && c.Cmc == seek.Cmc
                    && c.Artist.CompareTo(seek.Artist) < 0

                || c.Name == seek.Name
                    && c.SetName == seek.SetName
                    && c.Cmc == seek.Cmc
                    && c.Artist == seek.Artist
                    && c.Id.CompareTo(seek.Id) > 0));
    }


    [Fact]
    public async Task ToSeekList_WrongIndex_ReturnsFirst()
    {
        // only way in seek paging to know at the wrong index
        // is when page index is 0 and has a next

        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var seek = await cards
            .Skip(pageSize * numPages)
            .FirstAsync();

        var seekList = await cards
            .ToSeekListAsync(0, pageSize, seek.Id, true);

        var firstCards = await cards
            .Select(c => c.Id)
            .Take(pageSize)
            .ToListAsync();

        Assert.Null(seekList.Seek.Previous);
        Assert.NotNull(seekList.Seek.Next);

        Assert.Equal(pageSize, seekList.Count);
        Assert.Equal(firstCards, seekList.Select(c => c.Id));
    }


    [Fact]
    public async Task ToSeekList_OrderByNullableProperties_Returns()
    {
        await _testGen.CreateChangesAsync();

        const int pageSize = 4;
        const int numPages = 2;

        var changes = _dbContext.Changes
            .Include(c => c.To)
            .Include(c => c.From)
            .OrderBy(c => c.To.Name)
                .ThenBy(c => c.From!.Name)
                .ThenBy(c => c.Id);

        var seek = await changes
            .Skip(pageSize * numPages)
            .FirstAsync();

        var seekList = await changes
            .ToSeekListAsync(numPages, pageSize, new Nullable<int>(seek.Id), false);

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(
                (c.To.Name, c.From?.Name, c.Id).CompareTo(
                    (seek.To.Name, seek.From?.Name, seek.Id)) > 0));
    }
}