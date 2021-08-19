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
        private readonly CardDbContext _context;

        public DeleteModel(UserManager<CardUser> userManager, CardDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }


        public Deck Deck { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var user = await _userManager.GetUserAsync(User);

            Deck = await _context.Decks
                .Include(l => l.Cards)
                    .ThenInclude(ca => ca.Card)
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id && l.Owner == user);

            if (Deck is null)
            {
                return NotFound();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            Deck = await _context.Decks
                .Include(l => l.Cards)
                    .ThenInclude(ca => ca.Card)
                .SingleOrDefaultAsync(l => l.Id == id);

            if (Deck is not null)
            {
                var availCards = Deck.Cards
                    .Select(ca => ca.Card.Id)
                    .Distinct()
                    .ToArray();

                var availables = await _context.Cards
                    .Where(c => availCards.Contains(c.Id))
                    // TODO: change return location
                    .Select(c => c.Amounts.First(ca => ca.Location.Type == Discriminator.Shared))
                    .ToDictionaryAsync(ca => ca.Card.Id);

                foreach(var ca in Deck.Cards)
                {
                    availables[ca.Card.Id].Amount += ca.Amount;
                }

                _context.RemoveRange(Deck.Cards);

                _context.Decks.Remove(Deck);

                await _context.SaveChangesAsync();
            }

            return RedirectToPage("./Index");
        }
    }
}
