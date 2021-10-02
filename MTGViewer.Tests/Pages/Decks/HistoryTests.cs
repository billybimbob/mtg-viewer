using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;
using Xunit;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Pages.Decks;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages.Decks
{
    public class HistoryTests : IAsyncLifetime
    {
        private readonly ServiceProvider _services;
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        private readonly HistoryModel _historyModel;
        private Transaction _transaction;

        public HistoryTests()
        {
            _services = TestFactory.ServiceProvider();
            _dbContext = TestFactory.CardDbContext(_services);
            _userManager = TestFactory.CardUserManager(_services);
            
            _historyModel = new(_dbContext, _userManager, Mock.Of<ILogger<HistoryModel>>());
        }


        public async Task InitializeAsync()
        {
            await _dbContext.SeedAsync(_userManager);
            _transaction = await _dbContext.CreateTransactionAsync();
        }


        public async Task DisposeAsync()
        {
            await _services.DisposeAsync();
            await _dbContext.DisposeAsync();
            _userManager.Dispose();
        }


        private IQueryable<Transaction> Transactions =>
            _dbContext.Transactions.AsNoTracking();


        [Fact]
        public async Task OnPost_InvalidTransaction_NoChange()
        {
            var change = _transaction.Changes.First();
            var ownedId = (change.To as Deck)?.OwnerId ?? (change.From as Deck)?.OwnerId;

            var invalidTransactionId = 0;

            await _historyModel.SetModelContextAsync(_userManager, ownedId);

            var result = await _historyModel.OnPostAsync(invalidTransactionId);
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

            var result = await _historyModel.OnPostAsync(_transaction.Id);
            var transactions = await Transactions.Select(t => t.Id).ToListAsync();

            Assert.IsType<NotFoundResult>(result);
            Assert.Contains(_transaction.Id, transactions);
        }


        [Fact]
        public async Task OnPost_ValidTransaction_RemovesTransaction()
        {
            var change = _transaction.Changes.First();
            var ownedId = (change.To as Deck)?.OwnerId ?? (change.From as Deck)?.OwnerId;

            await _historyModel.SetModelContextAsync(_userManager, ownedId);

            var result = await _historyModel.OnPostAsync(_transaction.Id);
            var transactions = await Transactions.Select(t => t.Id).ToListAsync();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.DoesNotContain(_transaction.Id, transactions);
        }
    }
}