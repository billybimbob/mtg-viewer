using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Transactions;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Transactions;

public class DetailsTests : IAsyncLifetime
{
    private readonly DetailsModel _detailsModel;
    private readonly CardDbContext _dbContext;
    private readonly ActionHandlerFactory _pageFactory;

    private readonly TestDataGenerator _testGen;
    private Transaction _transaction = default!;

    public DetailsTests(
        DetailsModel detailsModel,
        CardDbContext dbContext,
        ActionHandlerFactory pageFactory,
        TestDataGenerator testGen)
    {
        _detailsModel = detailsModel;
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
        string ownedId = (change.To as Deck)?.OwnerId ?? (change.From as Deck)!.OwnerId;

        const int invalidTransactionId = 0;

        await _pageFactory.AddPageContextAsync(_detailsModel, ownedId);

        var result = await _detailsModel.OnPostAsync(invalidTransactionId, default);
        var transactions = await Transactions.Select(t => t.Id).ToListAsync();

        Assert.IsType<NotFoundResult>(result);
        Assert.Contains(_transaction.Id, transactions);
    }

    [Fact]
    public async Task OnPost_InvalidUser_NoChange()
    {
        var change = _transaction.Changes.First();
        string? ownedId = (change.To as Deck)?.OwnerId ?? (change.From as Deck)?.OwnerId;

        string wrongUser = await _dbContext.Users
            .Select(u => u.Id)
            .FirstAsync(uid => uid != ownedId);

        await _pageFactory.AddPageContextAsync(_detailsModel, wrongUser);

        var result = await _detailsModel.OnPostAsync(_transaction.Id, default);
        var transactions = await Transactions.Select(t => t.Id).ToListAsync();

        Assert.IsType<ForbidResult>(result);
        Assert.Contains(_transaction.Id, transactions);
    }

    [Fact]
    public async Task OnPost_ValidTransaction_RemovesTransaction()
    {
        var change = _transaction.Changes.First();
        string ownedId = (change.To as Deck)?.OwnerId ?? (change.From as Deck)!.OwnerId;

        await _pageFactory.AddPageContextAsync(_detailsModel, ownedId);

        var result = await _detailsModel.OnPostAsync(_transaction.Id, default);
        var transactions = await Transactions.Select(t => t.Id).ToListAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.DoesNotContain(_transaction.Id, transactions);
    }
}
