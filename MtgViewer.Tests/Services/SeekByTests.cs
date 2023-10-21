using System.Linq;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.EntityFrameworkCore;

using Xunit;

using MtgViewer.Data;
using MtgViewer.Tests.Utils;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace MtgViewer.Tests.Services;

public sealed class SeekByTests : IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;

    public SeekByTests(CardDbContext dbContext, TestDataGenerator testGen)
    {
        _dbContext = dbContext;
        _testGen = testGen;
    }

    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();

    [Fact]
    public async Task ToList_OrderBySeekMultipleId_Returns()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize * numPages)
            .FirstAsync();

        var result = await cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == origin.Id)
                .Take(pageSize)
            .ToListAsync();

        Assert.All(result, c =>
            Assert.True(
                (c.Name, c.Id).CompareTo((origin.Name, c.Id)) > 0));
    }

    [Fact]
    public void ToListSync_OrderBySeekMultipleId_Returns()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var origin = cards
            .Skip(pageSize * numPages)
            .First();

        var result = cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == origin.Id)
                .Take(pageSize)
            .ToList();

        Assert.All(result, c =>
            Assert.True(
                (c.Name, c.Id).CompareTo((origin.Name, c.Id)) > 0));
    }

    [Fact]
    public async Task ToList_OrderByAfterSeekBy_DoesNotThrow()
    {
        await _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id)
            .SeekBy(SeekDirection.Forward)
            .OrderBy(c => c.Id)
            .ToListAsync();
    }

    [Fact]
    public async Task Any_OrderBySeekMultipleId_ReturnsTrue()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize * numPages)
            .FirstAsync();

        bool result = await cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == origin.Id)
                .Take(pageSize)
            .AnyAsync();

        Assert.True(result);
    }

    [Fact]
    public void AnySync_OrderBySeekMultipleId_ReturnsTrue()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var origin = cards
            .Skip(pageSize * numPages)
            .First();

        bool result = cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == origin.Id)
                .Take(pageSize)
            .Any();

        Assert.True(result);
    }

    [Fact]
    public async Task Any_OrderByInvalidId_ReturnsTrue()
    {
        const int pageSize = 4;

        bool result = await _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id)
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == "this is a invalid id")
                .Take(pageSize)
            .AnyAsync();

        Assert.True(result);
    }

    [Fact]
    public void AnySync_OrderByInvalidId_ReturnsTrue()
    {
        const int pageSize = 4;

        bool result = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id)
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == "this is a invalid id")
                .Take(pageSize)
            .Any();

        Assert.True(result);
    }

    [Fact]
    public void Any_DynamicOrderBy_ReturnsTrue()
    {
        const int pageSize = 4;

        var query = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id)
            .SeekBy(SeekDirection.Forward)
                .Take(pageSize);

        object? result = query.Provider
            .Execute(
                Expression.Call(
                    instance: null,
                    QueryableMethods.AnyWithoutPredicate
                        .MakeGenericMethod(query.ElementType),
                    query.Expression));

        Assert.Equal(result, true);
    }

    [Fact]
    public void Any_DynamicQueryOrderBy_ReturnsTrue()
    {
        const int pageSize = 4;

        var query = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id)
            .SeekBy(SeekDirection.Forward);

        var takeQuery = query.Provider
            .CreateQuery(
                Expression.Call(
                    instance: null,
                    QueryableMethods.Take
                        .MakeGenericMethod(query.ElementType),
                    query.Expression,
                    Expression.Constant(pageSize)));

        object? result = takeQuery.Provider
            .Execute(
                Expression.Call(
                    instance: null,
                    QueryableMethods.AnyWithoutPredicate
                        .MakeGenericMethod(takeQuery.ElementType),
                    takeQuery.Expression));

        Assert.Equal(result, true);
    }

    [Fact]
    public async Task All_OrderBySeekMultipleId_ReturnsTrue()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize * numPages)
            .FirstAsync();

        bool result = await cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == origin.Id)
                .Take(pageSize)

            .AllAsync(c => c.Name.CompareTo(origin.Name) > 0
                || (c.Name == origin.Name && c.Id.CompareTo(origin.Id) > 0));

        Assert.True(result);
    }

    [Fact]
    public void AllSync_OrderBySeekMultipleId_ReturnsTrue()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var origin = cards
            .Skip(pageSize * numPages)
            .First();

        bool result = cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == origin.Id)
                .Take(pageSize)

            .All(c => c.Name.CompareTo(origin.Name) > 0
                || (c.Name == origin.Name && c.Id.CompareTo(origin.Id) > 0));

        Assert.True(result);
    }

    [Fact]
    public async Task FirstOrDefault_OrderBySeekMultipleId_Returns()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var origin = await cards
            .Skip(pageSize * numPages)
            .FirstAsync();

        var result = await cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == origin.Id)
                .Take(pageSize)
            .FirstOrDefaultAsync();

        Assert.NotNull(result);
        Assert.True((result.Name, result.Id).CompareTo((origin.Name, result.Id)) > 0);
    }

    [Fact]
    public void FirstOrDefaultSync_OrderBySeekMultipleId_Returns()
    {
        const int pageSize = 4;
        const int numPages = 2;

        var cards = _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.Id);

        var origin = cards
            .Skip(pageSize * numPages)
            .First();

        var result = cards
            .SeekBy(SeekDirection.Forward)
                .After(c => c.Id == origin.Id)
                .Take(pageSize)
            .FirstOrDefault();

        Assert.NotNull(result);
        Assert.True((result.Name, result.Id).CompareTo((origin.Name, result.Id)) > 0);
    }
}
