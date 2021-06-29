using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

using System.Threading.Tasks;

using MTGViewer.Data;


namespace MTGViewer.Pages.Cards
{
    [Authorize]
    public class DeleteModel : PageModel
    {
        private readonly CardDbContext _context;

        public DeleteModel(CardDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Card Card { get; set; }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            Card = await _context.Cards.FindAsync(id);

            if (Card == null)
            {
                return NotFound();
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            Card = await _context.Cards.FindAsync(id);

            if (Card != null)
            {
                _context.Cards.Remove(Card);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage("./Index");
        }
    }
}
