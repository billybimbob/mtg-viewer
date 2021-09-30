using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

#nullable enable

namespace MTGViewer.Pages.Decks
{
    [Authorize]
    public class ChangesModel : PageModel
    {
        private readonly CardDbContext _dbContext;
        private readonly ISharedStorage _sharedStorage;
        private readonly UserManager<CardUser> _userManager;
        private readonly ILogger<ChangesModel> _logger;

        public ChangesModel(
            CardDbContext dbContext, 
            ISharedStorage sharedStorage,
            UserManager<CardUser> userManager,
            ILogger<ChangesModel> logger)
        {
            _dbContext = dbContext;
            _sharedStorage = sharedStorage;
            _userManager = userManager;
            _logger = logger;
        }


        [BindProperty]
        public Deck? Deck { get; set; }

        public IReadOnlyList<Transaction>? TransactionsTo { get; private set; }

        public IReadOnlyList<Transaction>? TransactionsFrom { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            Deck = await DeckWithChanges(deckId).SingleOrDefaultAsync();

            if (Deck == default)
            {
                return NotFound();
            }

            TransactionsTo = Deck.ChangesTo
                .GroupBy(c => c.Transaction, (t, _) => t)
                .ToList();

            TransactionsFrom = Deck.ChangesFrom
                .GroupBy(c => c.Transaction, (t, _) => t)
                .ToList();

            return Page();
        }


        private IQueryable<Deck> DeckWithChanges(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            return _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId)

                .Include(d => d.ChangesTo)
                    .ThenInclude(c => c.Transaction)

                .Include(d => d.ChangesTo)
                    .ThenInclude(c => c.From)
                .Include(d => d.ChangesTo)
                    .ThenInclude(c => c.Card)

                .Include(d => d.ChangesTo
                    .OrderBy(c => c.Transaction.Applied)
                        .ThenBy(c => c.Card.Name))

                .Include(d => d.ChangesFrom)
                    .ThenInclude(c => c.Transaction)

                .Include(d => d.ChangesFrom)
                    .ThenInclude(c => c.To)
                .Include(d => d.ChangesFrom)
                    .ThenInclude(c => c.Card)

                .Include(d => d.ChangesFrom
                    .OrderBy(c => c.Transaction.Applied)
                        .ThenBy(c => c.Card.Name))
                        
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }


        // public async Task<IActionResult> OnPostRemoveAsync(int transactionId)
        public async Task<IActionResult> OnPostAsync(int transactionId)
        {
            var transaction = await _dbContext.Transactions
                .Include(t => t.Changes)
                    .ThenInclude(c => c.To)
                .Include(t => t.Changes)
                    .ThenInclude(c => c.From)
                .SingleOrDefaultAsync(t => t.Id == transactionId);

            if (transaction == default)
            {
                return RedirectToPage("Index");
            }

            var userId = _userManager.GetUserId(User);

            bool IsValidLocation(Location? loc)
            {
                return loc is Box or null
                    || loc is Deck deck && deck.OwnerId == userId;
            }

            var isValidUser = transaction.Changes.All(c => 
                IsValidLocation(c.To) && IsValidLocation(c.From));

            if (!isValidUser)
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
                _logger.LogError($"issue removing changes {e}");
            }

            return RedirectToPage("Changes", new { deckId = Deck?.Id });
        }


        // public async Task<IActionResult> OnPostUndoAsync(int transactionId)
        // {
        // }
    }
}