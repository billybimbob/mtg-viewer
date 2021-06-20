using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;

using System.Threading.Tasks;

using MTGViewer.Data;


namespace MTGViewer.Pages.Decks
{
    [Authorize]
    public class DeleteModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly MTGCardContext _context;

        public DeleteModel(UserManager<CardUser> userManager, MTGCardContext context)
        {
            _userManager = userManager;
            _context = context;
        }


        public Location Deck { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var user = await _userManager.GetUserAsync(User);

            Deck = await _context.Locations
                .Include(l => l.Cards)
                .ThenInclude(ca => ca.Card)
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id && l.Owner == user);

            if (Deck == null)
            {
                return NotFound();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            Deck = await _context.Locations.FindAsync(id);

            if (Deck != null)
            {
                _context.Locations.Remove(Deck);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage("./Index");
        }
    }
}
