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

#nullable enable

namespace MTGViewer.Pages.Decks
{
    [Authorize]
    public class HistoryModel : PageModel
    {
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;
        private readonly ILogger<HistoryModel> _logger;

        public HistoryModel(
            CardDbContext dbContext, 
            UserManager<CardUser> userManager,
            ILogger<HistoryModel> logger)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _logger = logger;
        }


        [TempData]
        public string? PostMessage { get; set; }

        [BindProperty]
        public Deck? Deck { get; set; }

        public IReadOnlyList<Transaction>? Transactions { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            Deck = await DeckForHistory(deckId).SingleOrDefaultAsync();

            if (Deck == default)
            {
                return NotFound();
            }

            var toTransactions = Deck.ChangesTo.Select(c => c.Transaction);
            var fromTransactions = Deck.ChangesFrom.Select(c => c.Transaction);

            Transactions = toTransactions
                .Union(fromTransactions)
                .OrderByDescending(t => t.Applied)
                .ToList();

            // db sort not working
            foreach (var transaction in Transactions)
            {
                transaction.Changes.Sort(
                    (c1, c2) => c1.Card.Name.CompareTo(c2.Card.Name));
            }

            return Page();
        }


        private IQueryable<Deck> DeckForHistory(int deckId)
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
                    .OrderBy(c => c.Card.Name))

                .Include(d => d.ChangesFrom)
                    .ThenInclude(c => c.Transaction)
                .Include(d => d.ChangesFrom)
                    .ThenInclude(c => c.To)
                .Include(d => d.ChangesFrom)
                    .ThenInclude(c => c.Card)

                .Include(d => d.ChangesFrom
                    .OrderBy(c => c.Card.Name))
                        
                .OrderBy(b => b.Id)
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
                return NotFound();
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

            return RedirectToPage("History", new { deckId = Deck?.Id });
        }


        // public async Task<IActionResult> OnPostUndoAsync(int transactionId)
        // {
        // }
    }
}