using System.Linq;

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
            Deck = await _context.Locations
                .Include(l => l.Cards)
                .ThenInclude(ca => ca.Card)
                .SingleOrDefaultAsync(l => l.Id == id);

            if (Deck != null)
            {
                var availCards = Deck.Cards
                    .Select(ca => ca.Card.Id)
                    .Distinct()
                    .ToArray();

                var availables = await _context.Cards
                    .Where(c => availCards.Contains(c.Id))
                    .Select(c => c.Amounts.Single(ca => ca.Location == null))
                    .ToDictionaryAsync(ca => ca.Card.Id);

                foreach(var ca in Deck.Cards)
                {
                    availables[ca.Card.Id].Amount += ca.Amount;
                }

                _context.RemoveRange(Deck.Cards);

                _context.Locations.Remove(Deck);

                await _context.SaveChangesAsync();
            }

            return RedirectToPage("./Index");
        }
    }
}