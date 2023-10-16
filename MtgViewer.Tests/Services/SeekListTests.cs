using System;
using System.Linq;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.EntityFrameworkCore;

using Xunit;

using MtgViewer.Data;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Services;

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
    public async Task ToSeekList_OrderByFirst_ReturnsFirst()
    {
        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        int pageSize = Math.Min(10, await cards.CountAsync() / 2);

        var seekList = await cards
            .SeekBy(SeekDirection.Forward)
                .After(null as Card)
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
    public void ToSeekListSync_OrderByFirst_ReturnsFirst()
    {
        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        int pageSize = Math.Min(10, cards.Count() / 2);

        var seekList = cards
            .SeekBy(SeekDirection.Forward)
                .After(null as Card)
                .Take(pageSize)
            .ToSeekList();

        var firstCards = cards
            .Select(c => c.Id)
            .Take(pageSize)
            .ToList();

        Assert.Null(seekList.Seek.Previous);
        Assert.NotNull(seekList.Seek.Next);

        Assert.Equal(pageSize, seekList.Count);
        Assert.Equal(firstCards, seekList.Select(c => c.Id));
    }

    [Fact]
    public async Task ToSeekList_OrderByFirstId_ReturnsFirst()
    {
        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        int pageSize = Math.Min(10, await cards.CountAsync() / 2);

        var seekList = await cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == null)
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
    public async Task ToSeekList_OrderByFirstIdEnumerableSource_ReturnsFirst()
    {
        var cardSource = await _dbContext.Cards.ToListAsync();

        var cards = cardSource
            .AsQueryable()
            .OrderBy(c => c.Id);

        int pageSize = Math.Min(10, cards.Count() / 2);

        var seekList = cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == null)
                .Take(pageSize)
            .ToSeekList();

        var firstCards = cards
            .Select(c => c.Id)
            .Take(pageSize)
            .ToList();

        Assert.Null(seekList.Seek.Previous);
        Assert.NotNull(seekList.Seek.Next);

        Assert.Equal(pageSize, seekList.Count);
        Assert.Equal(firstCards, seekList.Select(c => c.Id));
    }

    [Fact]
    public async Task ToSeekList_OrderByLastId_ReturnsLast()
    {
        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        string? lastOrigin = null;

        int cardCount = await cards.CountAsync();
        int pageSize = Math.Min(10, cardCount / 2);

        var seekList = await cards
            .SeekBy(SeekDirection.Backwards)
                .After(c => c.Id == lastOrigin)
                .Take(pageSize)
            .ToSeekListAsync();

        var lastCards = await cards
            .Select(c => c.Id)
            .Skip(cardCount - pageSize)
            .ToListAsync();

        Assert.NotNull(seekList.Seek.Previous);
        Assert.Null(seekList.Seek.Next);

        Assert.Equal(pageSize, seekList.Count);
        Assert.Equal(lastCards, seekList.Select(c => c.Id));
    }

    [Fact]
    public async Task ToSeekList_NoSeekBy_ReturnsFirst()
    {
        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        var firstCards = await cards
            .Select(c => c.Id)
            .ToListAsync();

        var seekList = await cards
            .Select(c => c.Id)
            .ToSeekListAsync();

        Assert.Null(seekList.Seek.Previous);
        Assert.Null(seekList.Seek.Next);

        Assert.Equal(firstCards, seekList);
    }

    [Fact]
    public async Task ToSeekList_OrderBySeek_Returns()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize)
            .FirstAsync();

        var seekList = await cards
            .SeekBy(SeekDirection.Forward)
                .After(origin)
                .Take(pageSize)
            .ToSeekListAsync();

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(c.Id.CompareTo(origin.Id) > 0));
    }

    [Fact]
    public void ToSeekListSync_OrderBySeek_Returns()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        var origin = cards
            .Skip(pageSize)
            .First();

        var seekList = cards
            .SeekBy(SeekDirection.Forward)
                .After(origin)
                .Take(pageSize)
            .ToSeekList();

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(c.Id.CompareTo(origin.Id) > 0));
    }

    [Fact]
    public async Task ToSeekList_OrderBySeekId_Returns()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        string? originId = await cards
            .Skip(pageSize)
            .Select(c => c.Id)
            .FirstAsync();

        var seekList = await cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == originId)
                .Take(pageSize)
            .ToSeekListAsync();

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(c.Id.CompareTo(originId) > 0));
    }

    [Fact]
    public async Task ToSeekList_OrderBySeekBackwards_Returns()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize)
            .FirstAsync();

        var seekList = await cards
            .SeekBy(SeekDirection.Backwards)
                .After(origin)
                .Take(pageSize)
            .ToSeekListAsync();

        Assert.Null(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(c.Id.CompareTo(origin.Id) < 0));
    }

    [Fact]
    public void ToSeekListSync_OrderBySeekBackwards_Returns()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        var origin = cards
            .Skip(pageSize)
            .First();

        var seekList = cards
            .SeekBy(SeekDirection.Backwards)
                .After(origin)
                .Take(pageSize)
            .ToSeekList();

        Assert.Null(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(c.Id.CompareTo(origin.Id) < 0));
    }

    [Fact]
    public async Task ToSeekList_OrderBySeekMultiple_Returns()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize * numPages)
            .FirstAsync();

        var seekList = await cards
            .SeekBy(SeekDirection.Forward)
                .After(origin)
                .Take(pageSize)
            .ToSeekListAsync();

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(
                (c.Name, c.Id).CompareTo((origin.Name, c.Id)) > 0));
    }

    [Fact]
    public async Task ToSeekList_OrderBySeekMultipleId_Returns()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize * numPages)
            .FirstAsync();

        var seekList = await cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == origin.Id)
                .Take(pageSize)
            .ToSeekListAsync();

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(
                (c.Name, c.Id).CompareTo((origin.Name, c.Id)) > 0));
    }

    [Fact]
    public async Task ToSeekList_OrderBySeekMultipleBackwards_Returns()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize * numPages)
            .FirstAsync();

        var seekList = await cards
            .SeekBy(SeekDirection.Backwards)
                .After(origin)
                .Take(pageSize)
            .ToSeekListAsync();

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(
                (c.Name, c.Id).CompareTo((origin.Name, c.Id)) < 0));
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

        var origin = await cards
            .Skip(pageSize)
            .FirstAsync();

        var seekList = await cards
            .SeekBy(SeekDirection.Forward)
                .After(origin)
                .Take(pageSize)
            .ToSeekListAsync();

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(
                c.Name.CompareTo(origin.Name) > 0

                || (c.Name == origin.Name
                    && c.SetName.CompareTo(origin.SetName) > 0)

                || (c.Name == origin.Name
                    && c.SetName == origin.SetName
                    && c.ManaValue < origin.ManaValue)

                || (c.Name == origin.Name
                    && c.SetName == origin.SetName
                    && c.ManaValue == origin.ManaValue
                    && c.Artist.CompareTo(origin.Artist) < 0)

                || (c.Name == origin.Name
                    && c.SetName == origin.SetName
                    && c.ManaValue == origin.ManaValue
                    && c.Artist == origin.Artist
                    && c.Id.CompareTo(origin.Id) > 0)));
    }

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

        var origin = await changes
            .Skip(pageSize * numPages)
            .FirstAsync();

        var seekList = await changes
            .SeekBy(SeekDirection.Forward)
                .After(origin)
                .Take(pageSize)
            .ToSeekListAsync();

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c =>
            Assert.True(
                (c.To.Name, c.From?.Name, c.Id).CompareTo(
                    (origin.To.Name, origin.From?.Name, origin.Id)) > 0));
    }

    [Fact]
    public async Task ToSeekList_SeekByOriginId_Returns()
    {
        await _testGen.CreateChangesAsync();

        const int pageSize = 3;
        const int numPages = 1;

        var changes = _dbContext.Changes
            .Include(c => c.To)
            .Include(c => c.From)
            .OrderBy(c => c.Id);

        int originId = await changes
            .Skip(pageSize * numPages)
            .Select(c => c.Id)
            .FirstAsync();

        var seekList = await changes
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == originId)
                .Take(pageSize)
            .ToSeekListAsync();

        Assert.NotNull(seekList.Seek.Previous);

        Assert.All(seekList, c => Assert.True(c.Id >= originId));
    }

    [Fact]
    public async Task ToSeekList_OrderByAfterSeek_DoesNotThrow()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize)
            .FirstAsync();

        var _ = cards
            .SeekBy(SeekDirection.Forward)
                .After(origin)
                .Take(pageSize)
            .OrderBy(c => c.Id)
            .ToSeekListAsync();
    }

    [Fact]
    public async Task ToSeekList_OrderByThenByAfterSeek_DoesNotThrow()
    {
        const int pageSize = 4;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize)
            .FirstAsync();

        var _ = await cards
            .SeekBy(SeekDirection.Forward)
                .After(origin)
                .Take(pageSize)
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id)
            .ToSeekListAsync();
    }

    [Fact]
    public async Task ToSeekList_MultipleSeekBy_DoesNotThrow()
    {
        const int pageSize = 4;
        const int secondPageSize = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize)
            .FirstAsync();

        var origin2 = await cards
            .Skip(pageSize + 1)
            .FirstAsync();

        var _ = await cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == origin.Id)
                .Take(pageSize)

            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id)

            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == origin2.Id)
                .Take(secondPageSize)

            .ToSeekListAsync();
    }

    [Fact]
    public async Task ToSeekList_MultipleSeekByBackwards_DoesNotThrow()
    {
        const int pageSize = 4;
        const int secondPageSize = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize)
            .FirstAsync();

        var innerSeekBy = cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == origin.Id)
                .Take(pageSize);

        var innerCards = await innerSeekBy.ToListAsync();
        var origin2 = innerCards[^2];

        var _ = await innerSeekBy
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id)

            .SeekBy(SeekDirection.Backwards)
                .After(c => c.Id == origin2.Id)
                .Take(secondPageSize)

            .ToSeekListAsync();
    }
}
