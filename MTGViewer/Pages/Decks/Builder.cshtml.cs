using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Decks
{
    [Authorize]
    public class BuilderModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public BuilderModel(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
        }


        public string UserId { get; private set; }
        public int DeckId { get; private set; }


        public async Task<IActionResult> OnGetAsync(int? id)
        {
            UserId = _userManager.GetUserId(User);

            if (id is int deckId)
            {
                var deck = await _dbContext.Decks
                    .Include(d => d.TradesTo
                        .Where(t => t.ProposerId == UserId))
                    .AsNoTrackingWithIdentityResolution()
                    .SingleOrDefaultAsync(l => l.Id == deckId && l.Owner.Id == UserId);

                if (deck == default || deck.TradesTo.Any())
                {
                    return NotFound();
                }

                DeckId = deckId;
            }
            else
            {
                DeckId = default;
            }

            // the deck cannot be used as a param because of cyclic refs
            return Page();
        }

    }
}