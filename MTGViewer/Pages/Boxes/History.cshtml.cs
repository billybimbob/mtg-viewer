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

        public IReadOnlyList<BoxAndTransactions>? Boxes { get; private set; }

        public IReadOnlySet<int>? IsSharedTransaction { get; private set; }


        public async Task OnGetAsync()
        {
            var boxes = await BoxForHistory().ToListAsync();

            Boxes = boxes.Select(WithTransactions).ToList();

            IsSharedTransaction = boxes
                .SelectMany(b => b.GetChanges())
                .Where(IsShared)
                .Select(c => c.TransactionId)
                .ToHashSet();
        }


        private IQueryable<Box> BoxForHistory()
        {
            // order by doesn't seem to work, possible bug?
            return _dbContext.Boxes

                .Include(b => b.ChangesTo)
                    .ThenInclude(c => c.Transaction)
                .Include(b => b.ChangesTo)
                    .ThenInclude(c => c.Card)
                .Include(b => b.ChangesTo)
                    .ThenInclude(c => c.From)

                .Include(b => b.ChangesTo
                    .OrderBy(c => c.Card.Name))

                .Include(b => b.ChangesFrom)
                    .ThenInclude(c => c.Transaction)
                .Include(b => b.ChangesFrom)
                    .ThenInclude(c => c.Card)
                .Include(b => b.ChangesFrom)
                    .ThenInclude(c => c.To)

                .Include(b => b.ChangesFrom
                    .OrderBy(c => c.Card.Name))
                        
                .OrderBy(b => b.Id)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }


        // private void 


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