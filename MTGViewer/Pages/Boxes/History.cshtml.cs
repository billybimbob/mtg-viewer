using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;

#nullable enable

namespace MTGViewer.Pages.Boxes
{
    public record BoxAndTransactions(Box Box, IReadOnlyList<Transaction> Transactions) { }


    public class HistoryModel : PageModel
    {
        private readonly CardDbContext _dbContext;
        private readonly ILogger<HistoryModel> _logger;

        public HistoryModel(CardDbContext dbContext, ILogger<HistoryModel> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }


        [TempData]
        public string? PostMessage { get; set; }

        public IReadOnlyList<Transfer>? Transfers { get; private set; }

        public IReadOnlySet<(int, int?, int)>? IsFirstTransfer { get; private set; }
        public IReadOnlySet<int>? IsSharedTransaction { get; private set; }


        public async Task OnGetAsync()
        {
            var changes = await ChangesForHistory().ToListAsync();

            Transfers = changes
                .GroupBy(c => (c.Transaction, c.From, c.To),
                    (tof, changes) => new Transfer(
                        tof.Transaction, 
                        tof.From, 
                        tof.To, 
                        changes.ToList()))
                .ToList();

            IsFirstTransfer = changes
                .Select(c => (c.TransactionId, c.FromId, c.ToId))
                .ToHashSet();

            IsSharedTransaction = changes
                .Where(IsShared)
                .Select(c => c.TransactionId)
                .ToHashSet();
        }


        private IQueryable<Change> ChangesForHistory()
        {
            return _dbContext.Changes
                .Where(c => c.To is Box || c.From is Box)

                .Include(c => c.Transaction)
                .Include(c => c.From)
                .Include(c => c.To)
                .Include(c => c.Card)

                .OrderByDescending(c => c.Transaction.Applied)
                    .ThenBy(c => c.From!.Name)
                    .ThenBy(c => c.To.Name)
                        .ThenBy(c => c.Card.Name)
                        .ThenBy(c => c.Amount)
                        
                .AsNoTrackingWithIdentityResolution();
        }


        private BoxAndTransactions WithTransactions(Box box)
        {
            var toTransactions = box.ChangesTo.Select(c => c.Transaction);
            var fromTransactions = box.ChangesFrom.Select(c => c.Transaction);

            var transactions = toTransactions
                .Union(fromTransactions)
                .OrderByDescending(t => t.Applied)
                .ToList();

            // db sort not working
            foreach (var transaction in transactions)
            {
                transaction.Changes.Sort(
                    (c1, c2) => c1.Card.Name.CompareTo(c2.Card.Name));
            }

            return new BoxAndTransactions(box, transactions);
        }


        private bool IsShared(Change change) => 
            change.To is Box && change.From is Box or null;


        public async Task<IActionResult> OnPostAsync(int transactionId)
        {
            var transaction = await _dbContext.Transactions
                .Include(t => t.Changes)
                    .ThenInclude(c => c.To)
                .Include(t => t.Changes)
                    .ThenInclude(c => c.From)
                .SingleOrDefaultAsync(t => t.Id == transactionId);

            if (transaction.Changes.Any(c => !IsShared(c)))
            {
                return NotFound();
            }

            _dbContext.Transactions.Remove(transaction);
            _dbContext.Changes.RemoveRange(transaction.Changes);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Successfully removed the transaction";
            }
            catch (DbUpdateException e)
            {
                _logger.LogError($"ran into error while removing the transaction {e}");
                PostMessage = "Ran into issue while removing transaction";
            }

            return RedirectToPage("History");
        }
    }
}