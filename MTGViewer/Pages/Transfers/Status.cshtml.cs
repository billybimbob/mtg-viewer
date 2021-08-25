using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Transfers
{
    [Authorize]
    public class StatusModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public StatusModel(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
        }


        [TempData]
        public string PostMessage { get; set; }

        public CardUser Proposer { get; private set; }
        public Deck Destination { get; private set; }
        public IReadOnlyList<Trade> Trades { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            if (deckId == default)
            {
                return NotFound();
            }

            var userId = _userManager.GetUserId(User);
            
            var deck = await _dbContext.Decks
                .Include(d => d.Owner)
                .SingleOrDefaultAsync(d =>
                    d.Id == deckId && d.OwnerId == userId);

            if (deck == default)
            {
                return NotFound();
            }

            var deckTrades = await _dbContext.Trades
                .Where(t => t.ProposerId == userId && t.ToId == deckId)
                .Include(t => t.Card)
                .Include(t => t.From)
                .OrderBy(t => t.Card.Name)
                .ToListAsync();

            if (!deckTrades.Any())
            {
                PostMessage = "No trades were found";
                return RedirectToPage("./Index");
            }

            Proposer = deck.Owner;
            Destination = deck;
            Trades = deckTrades;

            return Page();
        }


        public async Task<IActionResult> OnPostAsync(int deckId)
        {
            if (deckId == default)
            {
                PostMessage = "Deck is not valid";
                return RedirectToPage("./Index");
            }

            var userId = _userManager.GetUserId(User);
            var validDeck = await _dbContext.Decks
                .Include(d => d.Owner)
                .AnyAsync(d => d.Id == deckId && d.OwnerId == userId);

            if (!validDeck)
            {
                PostMessage = "Deck is not valid";
                return RedirectToPage("./Index");
            }

            var deckTrades = await _dbContext.Trades
                .Where(t => t.ProposerId == userId && t.ToId == deckId)
                .ToListAsync();

            _dbContext.Trades.RemoveRange(deckTrades);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Successfully cancelled requests";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into error while cancelling";
            }

            return RedirectToPage("./Index");
        }
    }
}