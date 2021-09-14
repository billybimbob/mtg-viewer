using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Decks
{
    public class ViewerModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public ViewerModel(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
        }


        public bool CanEdit { get; private set; }
        public Deck Deck { get; private set; }
        public IEnumerable<SameAmountPair> Amounts { get; private set; }

        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            Deck = await _dbContext.Decks
                .Include(d => d.Owner)
                .Include(d => d.Cards
                    .OrderBy(ca => ca.Card.Name))
                    .ThenInclude(ca => ca.Card)
                .Include(d => d.TradesTo
                    .Where(t => t.ProposerId == t.To.OwnerId))
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync(d => d.Id == deckId);

            if (Deck == default)
            {
                return NotFound();
            }

            var userId = _userManager.GetUserId(User);

            CanEdit = Deck.OwnerId == userId && !Deck.TradesTo.Any();

            Amounts = Deck.Cards
                .GroupBy(ca => ca.CardId,
                    (_, amounts) => new SameAmountPair(amounts))
                .ToList();

            return Page();
        }
    }
}