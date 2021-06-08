using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MTGViewer.Models;

namespace MTGViewer.Pages.Cards
{
    public class DetailsModel : PageModel
    {
        private readonly MTGCardContext _context;

        public DetailsModel(MTGCardContext context)
        {
            _context = context;
        }

        public Card Card { get; private set; }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            Card = await _context.Cards.FirstOrDefaultAsync(m => m.Id == id);

            if (Card == null)
            {
                return NotFound();
            }
            else
            {
                var colors = string.Join(',', Card.GetColorSymbols());
                Console.WriteLine($"colors for {Card.Name} are {colors}");
                return Page();
            }
        }
    }
}
