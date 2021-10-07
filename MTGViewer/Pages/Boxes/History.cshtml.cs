using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;
using MTGViewer.Services;

#nullable enable

namespace MTGViewer.Pages.Boxes
{
    public class HistoryModel : PageModel
    {
        private readonly int _pageSize;
        private readonly CardDbContext _dbContext;
        private readonly ILogger<HistoryModel> _logger;

        public HistoryModel(
            PageSizes pageSizes,
            CardDbContext dbContext,
            ILogger<HistoryModel> logger)
        {
            _pageSize = pageSizes.GetSize(this);
            _dbContext = dbContext;
            _logger = logger;
        }


        [TempData]
        public string? PostMessage { get; set; }

        public IReadOnlyList<Transfer>? Transfers { get; private set; }

        public Data.Pages Pages { get; private set; }

        public IReadOnlySet<(int, int?, int)>? IsFirstTransfer { get; private set; }

        public IReadOnlySet<int>? IsSharedTransaction { get; private set; }


        public async Task OnGetAsync(int? pageIndex)
        {
            var changes = await ChangesForHistory()
                .ToPagedListAsync(_pageSize, pageIndex);

            Transfers = changes
                .GroupBy(c => (c.Transaction, c.From, c.To),
                    (tft, changes) =>
                        new Transfer(tft.Transaction, tft.From, tft.To, changes.ToList()) )
                .ToList();

            Pages = changes.Pages;

            IsFirstTransfer = changes
                .Select(c => (c.TransactionId, c.FromId, c.ToId))
                .GroupBy(tft => tft.TransactionId,
                    (_, tfts) => tfts.First())
                .ToHashSet();

            IsSharedTransaction = changes
                .Where(IsShared)
                .Select(c => c.TransactionId)
                .ToHashSet();
        }


        private IQueryable<Change> ChangesForHistory()
        {
            return _dbContext.Changes
                .Where(c => c.From is Box || c.To is Box)

                .Include(c => c.Transaction)
                .Include(c => c.From)
                .Include(c => c.To)
                .Include(c => c.Card)

                .OrderByDescending(c => c.Transaction.AppliedAt)
                    .ThenBy(c => c.From!.Name)
                    .ThenBy(c => c.To.Name)
                        .ThenBy(c => c.Card.Name)
                        .ThenBy(c => c.Amount)
                        
                .AsNoTrackingWithIdentityResolution();
        }


        private bool IsShared(Change change) => 
            change.To is Box && change.From is Box or null;



        public async Task<IActionResult> OnPostAsync(int transactionId)
        {
            var transaction = await _dbContext.Transactions
                .Include(t => t.Changes)
                    .ThenInclude(c => c.From)
                .Include(t => t.Changes) // unbounded, keep eye on
                    .ThenInclude(c => c.To)
                .SingleOrDefaultAsync(t => t.Id == transactionId);

            if (transaction == default || transaction.Changes.Any(c => !IsShared(c)))
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