using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MTGViewer.Pages.Cards
{
    public class IndexModel : PageModel
    {
        public IndexModel()
        {
        }

        // public IList<Card> Cards { get; private set; }

        // [BindProperty]
        // public Card Search { get; set; }


        // public async Task OnGetAsync()
        // {
        //     Cards = await _context.Cards.ToListAsync();
        // }

        // public async Task OnPostAsync()
        // {
        //     var query = _context.Cards.Select(c => c);
        //     if (ModelState.IsValid)
        //     {
        //         query = query.Where(c => c.Name.Contains(Search.Name));
        //     }

        //     Cards = await query.ToListAsync();
        // }

    }
}
