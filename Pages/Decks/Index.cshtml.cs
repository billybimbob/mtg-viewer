using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Decks
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly MTGCardContext _context;

        public IndexModel(UserManager<CardUser> userManager, MTGCardContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public CardUser CardUser { get; private set; }
        public IEnumerable<Location> Decks { get; private set; }
        public IEnumerable<IEnumerable<string>> DeckColors { get; private set; }


        public async Task OnGet()
        {
            CardUser = await _userManager.GetUserAsync(User);

            Decks = await _context.Locations
                .Where(l => l.Owner == CardUser)
                .Include(l => l.Cards)
                .ThenInclude(ca => ca.Card)
                .AsNoTracking()
                .ToListAsync();

            DeckColors = Decks.Select(l => 
                l.Cards 
                    .SelectMany(ca => ca.Card
                    .GetColorSymbols()
                    .Select(s => s.ToLower()))
                .Where(c => Color.COLORS.Values.Contains(c))
                .Distinct()
                .OrderBy(c => c));
        }

    }
}