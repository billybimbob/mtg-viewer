using System.Linq;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;

using System.Threading.Tasks;

using MTGViewer.Data;


namespace MTGViewer.Pages.Decks
{
    [Authorize]
    public class DeleteModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;
        private readonly ILogger<DeleteModel> _logger;

        public DeleteModel(
            UserManager<CardUser> userManager, CardDbContext dbContext, ILogger<DeleteModel> logger)
        {
            _userManager = userManager;
            _dbContext = dbContext;
            _logger = logger;
        }


        public Deck Deck { get; private set; }


        public async Task<IActionResult> OnGetAsync(int id)
        {
            var user = await _userManager.GetUserAsync(User);

            Deck = await _dbContext.Decks
                .Include(l => l.Cards)
                    .ThenInclude(ca => ca.Card)
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == user.Id);

            if (Deck is null)
            {
                return NotFound();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            Deck = await _dbContext.Decks.FindAsync(id);

            if (Deck is null)
            {
                return RedirectToPage("./Index");
            }

            var deckAmounts = _dbContext.Amounts
                .Where(ca => ca.LocationId == Deck.Id);

            var sharedAmounts = _dbContext.Amounts
                .Where(ca => ca.Location is Data.Shared);

            var amountPairs = await deckAmounts
                .Join( sharedAmounts,
                    deck => deck.CardId,
                    shared => shared.CardId,
                    (deck, shared) => new { deck, shared })
                .ToListAsync();

            foreach(var pair in amountPairs)
            {
                if (!pair.deck.IsRequest)
                {
                    pair.shared.Amount += pair.deck.Amount;
                }
            }

            _dbContext.RemoveRange(Deck.Cards);
            _dbContext.Decks.Remove(Deck);

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                _logger.LogError(e.ToString());
            }

            return RedirectToPage("./Index");
        }
    }
}
