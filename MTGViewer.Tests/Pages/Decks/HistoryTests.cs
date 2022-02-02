using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Decks;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Decks;


public class HistoryTests : IAsyncLifetime
{
    private readonly HistoryModel _historyModel;
    private readonly CardDbContext _dbContext;
    private readonly PageContextFactory _pageFactory;

    private readonly TestDataGenerator _testGen;
    private Transaction _transaction = null!;


    public HistoryTests(
        HistoryModel historyModel,
        CardDbContext dbContext,
        PageContextFactory pageFactory,
        TestDataGenerator testGen)
    {
        _historyModel = historyModel;
        _dbContext = dbContext;
        _pageFactory = pageFactory;
        _testGen = testGen;
    }


    public async Task InitializeAsync()
    {
        await _testGen.SeedAsync();
        _transaction = await _testGen.CreateTransactionAsync();
    }


    public Task DisposeAsync() => _testGen.ClearAsync();


    private IQueryable<Transaction> Transactions =>
        _dbContext.Transactions.AsNoTracking();


    [Fact]
    public async Task OnPost_InvalidTransaction_NoChange()
    {
        var change = _transaction.Changes.First();
        var ownedId = (change.To as Deck)?.OwnerId ?? (change.From as Deck)!.OwnerId;

        var invalidTransactionId = 0;

        await _pageFactory.AddModelContextAsync(_historyModel, ownedId);

        var result = await _historyModel.OnPostAsync(invalidTransactionId, default);
        var transactions = await Transactions.Select(t => t.Id).ToListAsync();

        Assert.IsType<NotFoundResult>(result);
        Assert.Contains(_transaction.Id, transactions);
    }


    [Fact]
    public async Task OnPost_InvalidUser_NoChange()
    {
        var change = _transaction.Changes.First();
        var ownedId = (change.To as Deck)?.OwnerId ?? (change.From as Deck)?.OwnerId;

        var wrongUser = await _dbContext.Users
            .Where(u => u.Id != ownedId)
            .Select(u => u.Id)
            .FirstAsync();

        await _pageFactory.AddModelContextAsync(_historyModel, wrongUser);

        var result = await _historyModel.OnPostAsync(_transaction.Id, default);
        var transactions = await Transactions.Select(t => t.Id).ToListAsync();

        Assert.IsType<NotFoundResult>(result);
        Assert.Contains(_transaction.Id, transactions);
    }


    [Fact]
    public async Task OnPost_ValidTransaction_RemovesTransaction()
    {
        var change = _transaction.Changes.First();
        var ownedId = (change.To as Deck)?.OwnerId ?? (change.From as Deck)!.OwnerId;

        await _pageFactory.AddModelContextAsync(_historyModel, ownedId);

        var result = await _historyModel.OnPostAsync(_transaction.Id, default);
        var transactions = await Transactions.Select(t => t.Id).ToListAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.DoesNotContain(_transaction.Id, transactions);
    }
}