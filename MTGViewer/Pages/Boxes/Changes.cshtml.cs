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
    public record BoxAndTransactions(
        Box Box,
        IReadOnlyList<Transaction> Transactions) { }


    public class ChangesModel : PageModel
    {
        private readonly CardDbContext _dbContext;
        private readonly ILogger<ChangesModel> _logger;

        public ChangesModel(CardDbContext dbContext, ILogger<ChangesModel> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }


        public IReadOnlyList<BoxAndTransactions>? Boxes { get; private set; }

        public IReadOnlyDictionary<int, bool>? IsSharedTransaction { get; private set; }


        public async Task OnGetAsync()
        {
            var boxes = await BoxWithChanges().ToListAsync();

            Boxes = boxes.Select(WithTransactions).ToList();

            IsSharedTransaction = boxes
                .SelectMany(b => b.GetChanges())
                .GroupBy(c => c.Transaction, (t, _) => t)
                .ToDictionary(
                    t => t.Id, 
                    t => t.Changes.All(IsShared));
        }


        private IQueryable<Box> BoxWithChanges()
        {
            return _dbContext.Boxes
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()

                .Include(b => b.ChangesTo)
                    .ThenInclude(c => c.Transaction)

                .Include(b => b.ChangesTo)
                    .ThenInclude(c => c.Card)
                .Include(b => b.ChangesTo)
                    .ThenInclude(c => c.From)

                .Include(b => b.ChangesTo
                    .OrderBy(c => c.Transaction.Applied)
                        .ThenBy(c => c.Card.Name))

                .Include(b => b.ChangesFrom)
                    .ThenInclude(c => c.Transaction)

                .Include(b => b.ChangesFrom)
                    .ThenInclude(c => c.Card)
                .Include(b => b.ChangesFrom)
                    .ThenInclude(c => c.To)

                .Include(b => b.ChangesFrom
                    .OrderBy(c => c.Transaction.Applied)
                        .ThenBy(c => c.Card.Name))
                        
                .OrderBy(b => b.Id);
        }


        private BoxAndTransactions WithTransactions(Box box)
        {
            var tos = box.ChangesTo
                .GroupBy(c => c.Transaction, (t, _) => t);

            var froms = box.ChangesFrom
                .GroupBy(c => c.Transaction, (t, _) => t);

            var transactions = tos
                .Concat(froms)
                .ToList();

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
            }
            catch (DbUpdateException e)
            {
                _logger.LogError($"ran into error while removing the transaction {e}");
            }

            return RedirectToPage("Changes");
        }
    }
}