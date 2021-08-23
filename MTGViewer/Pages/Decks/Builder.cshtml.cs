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
        private readonly CardDbContext _context;

        public BuilderModel(UserManager<CardUser> userManager, CardDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }


        public CardUser CardUser { get; private set; }
        public int DeckId { get; private set; }


        public async Task<IActionResult> OnGetAsync(int? id)
        {
            CardUser = await _userManager.GetUserAsync(User);

            if (id is int deckId)
            {
                var isOwner = await _context.Decks
                    .AnyAsync(l => l.Id == deckId && l.Owner == CardUser);

                if (!isOwner)
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