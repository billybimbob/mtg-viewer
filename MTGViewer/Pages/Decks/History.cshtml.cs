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

        public IReadOnlyList<Transfer>? Transfers { get; private set; }

        public IReadOnlySet<(int, int?, int)>? IsFirstTransfer { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            var deck = await _dbContext.Decks
                .AsNoTracking()
                .SingleOrDefaultAsync(d => d.Id == deckId && d.OwnerId == userId);

            if (deck == default)
            {
                return NotFound();
            }

            var changes = await ChangesForHistory(deckId).ToListAsync();


            Deck = DeckFromChanges(deckId, changes) ?? deck;

            Transfers = changes
                .GroupBy(c => (c.Transaction, c.From, c.To),
                    (tof, changes) => 
                        new Transfer(tof.Transaction, tof.From, tof.To, changes.ToList()))
                .ToList();

            IsFirstTransfer = changes
                .Select(c => (c.TransactionId, c.FromId, c.ToId))
                .ToHashSet();


            return Page();
        }


        private IQueryable<Change> ChangesForHistory(int deckId)
        {
            return _dbContext.Changes
                .Where(c => c.ToId == deckId || c.FromId == deckId)
                
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


        private Deck? DeckFromChanges(int deckId, IReadOnlyList<Change> changes)
        {
            var deckFromChanges = changes
                .Select(c => c.To)
                .FirstOrDefault(l => l.Id == deckId);

            deckFromChanges ??= changes
                .Select(c => c.From)
                .FirstOrDefault(l => l?.Id == deckId);

            return deckFromChanges as Deck;
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