using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;
using Xunit;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Pages.Decks;
using MTGViewer.Services;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Decks;


public class HistoryTests : IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private readonly TestDataGenerator _testGen;

    private readonly HistoryModel _historyModel;
    private Transaction _transaction = null!;

    public HistoryTests(
        PageSizes pageSizes,
        CardDbContext dbContext,
        UserManager<CardUser> userManager,
        TestDataGenerator testGen)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _testGen = testGen;

        var logger = Mock.Of<ILogger<HistoryModel>>();
        
        _historyModel = new(pageSizes, _dbContext, _userManager, logger);
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

        await _historyModel.SetModelContextAsync(_userManager, ownedId);

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

        await _historyModel.SetModelContextAsync(_userManager, wrongUser);

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

        await _historyModel.SetModelContextAsync(_userManager, ownedId);

        var result = await _historyModel.OnPostAsync(_transaction.Id, default);
        var transactions = await Transactions.Select(t => t.Id).ToListAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.DoesNotContain(_transaction.Id, transactions);
    }
}