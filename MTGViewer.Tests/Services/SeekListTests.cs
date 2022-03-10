using System;
using System.Linq;
using System.Paging;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

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

        var seekList = await cards
            .SeekBy(null as string, SeekDirection.Forward)
            .Take(pageSize)
            .ToSeekListAsync();

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

        var seekList = await cards
            .SeekBy(null as string, SeekDirection.Forward)
            .Take(pageSize)
            .ToSeekListAsync();

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
            .FirstAsync();

        Task SeekListAsync() => cards
            .SeekOrigin(seek, SeekDirection.Forward)
            .Take(pageSize)
            .ToSeekListAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(SeekListAsync);
    }


    [Fact]
    public async Task ToSeekList_OrderBySeek_Returns()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards.OrderBy(c => c.Id);

        var seek = await cards
            .Skip(pageSize)
            .FirstAsync();

        var seekList = await cards
            .SeekOrigin(seek, SeekDirection.Forward)
            .Take(pageSize)
            .ToSeekListAsync();

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(c.Id.CompareTo(seek.Id) > 0));
    }


    [Fact]
    public async Task ToSeekList_OrderBySeekBackwards_Returns()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards.OrderBy(c => c.Id);

        var seek = await cards
            .Skip(pageSize)
            .FirstAsync();

        var seekList = await cards
            .SeekOrigin(seek, SeekDirection.Backwards)
            .Take(pageSize)
            .ToSeekListAsync();

        Assert.Null(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(c.Id.CompareTo(seek.Id) < 0));
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
            .SeekOrigin(seek, SeekDirection.Forward)
            .Take(pageSize)
            .ToSeekListAsync();

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
            .SeekOrigin(seek, SeekDirection.Backwards)
            .Take(pageSize)
            .ToSeekListAsync();

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
                .ThenBy(c => c.ManaValue == null)
                .ThenByDescending(c => c.ManaValue)
                .ThenByDescending(c => c.Artist)
                .ThenBy(c => c.Id);

        var seek = await cards
            .Skip(pageSize)
            .FirstAsync();

        var seekList = await cards
            .SeekOrigin(seek, SeekDirection.Forward)
            .Take(pageSize)
            .ToSeekListAsync();

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(
                c.Name.CompareTo(seek.Name) > 0

                || c.Name == seek.Name
                    && c.SetName.CompareTo(seek.SetName) > 0

                || c.Name == seek.Name
                    && c.SetName == seek.SetName
                    && c.ManaValue < seek.ManaValue

                || c.Name == seek.Name
                    && c.SetName == seek.SetName
                    && c.ManaValue == seek.ManaValue
                    && c.Artist.CompareTo(seek.Artist) < 0

                || c.Name == seek.Name
                    && c.SetName == seek.SetName
                    && c.ManaValue == seek.ManaValue
                    && c.Artist == seek.Artist
                    && c.Id.CompareTo(seek.Id) > 0));
    }


    // [Fact]
    // public async Task ToSeekList_WrongIndex_ReturnsFirst()
    // {
    //     // only way in seek paging to know at the wrong index
    //     // is when page index is 0 and has a next

    //     const int pageSize = 4;
    //     const int numPages = 2;

    //     var cards = _dbContext.Cards
    //         .OrderBy(c => c.Name)
    //             .ThenBy(c => c.Id);

    //     var seek = await cards
    //         .Skip(pageSize * numPages)
    //         .FirstAsync();

    //     var seekList = await cards
    //         .SeekBy(seek, pageSize, true)
    //         .ToSeekListAsync();

    //     var firstCards = await cards
    //         .Select(c => c.Id)
    //         .Take(pageSize)
    //         .ToListAsync();

    //     Assert.Null(seekList.Seek.Previous);
    //     Assert.NotNull(seekList.Seek.Next);

    //     Assert.Equal(pageSize, seekList.Count);
    //     Assert.Equal(firstCards, seekList.Select(c => c.Id));
    // }


    [Fact]
    public async Task ToSeekList_OrderByNullableProperties_Returns()
    {
        await _testGen.CreateChangesAsync();

        const int pageSize = 3;
        const int numPages = 1;

        var changes = _dbContext.Changes
            .Include(c => c.To)
            .Include(c => c.From)
            .OrderBy(c => c.To.Name)
                .ThenByDescending(c => c.From == null)
                .ThenBy(c => c.From!.Name)
                .ThenBy(c => c.Id);

        var seek = await changes
            .Skip(pageSize * numPages)
            .FirstAsync();

        var seekList = await changes
            .SeekOrigin(seek, SeekDirection.Forward)
            .Take(pageSize)
            .ToSeekListAsync();

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(
                (c.To.Name, c.From?.Name, c.Id).CompareTo(
                    (seek.To.Name, seek.From?.Name, seek.Id)) > 0));
    }
}