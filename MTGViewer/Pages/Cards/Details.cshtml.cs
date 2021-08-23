using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using System.Threading.Tasks;

using MTGViewer.Data;


namespace MTGViewer.Pages.Cards
{
    public class DetailsModel : PageModel
    {
        private readonly CardDbContext _context;

        public DetailsModel(CardDbContext context)
        {
            _context = context;
        }

        public Card Card { get; private set; }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (id is null)
            {
                return NotFound();
            }

            Card = await _context.Cards.FindAsync(id);

            if (Card is null)
            {
                return NotFound();
            }
            else
            {
                return Page();
            }
        }
    }
}
