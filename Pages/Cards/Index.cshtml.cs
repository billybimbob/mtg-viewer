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
    public class IndexModel : PageModel
    {
        private readonly MTGCardContext _context;

        public IndexModel(MTGCardContext context)
        {
            _context = context;
        }

        public IList<Card> Cards { get; private set; }

        public async Task OnGetAsync()
        {
            Cards = await _context.Cards.ToListAsync();
        }
    }
}
